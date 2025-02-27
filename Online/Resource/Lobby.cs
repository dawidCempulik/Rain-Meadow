﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace RainMeadow
{
    // Lobby is tightly bound to a SteamMatchmaking Lobby, also the top-level resource
    public class Lobby : OnlineResource
    {
        public OnlineGameMode gameMode;
        public OnlineGameMode.OnlineGameModeType gameModeType;
        public Dictionary<string, WorldSession> worldSessions = new();
        public Dictionary<OnlinePlayer, ClientSettings> clientSettings = new();
        public Dictionary<OnlinePlayer, OnlineEntity.EntityId> playerAvatars = new(); // should maybe be in GameMode

        public string[] mods = RainMeadowModManager.GetActiveMods();
        public static bool modsChecked;

        public string? password;
        public bool hasPassword => password != null;
        public Lobby(OnlineGameMode.OnlineGameModeType mode, OnlinePlayer owner, string? password)
        {
            this.super = this;
            OnlineManager.lobby = this; // needed for early entity processing

            this.gameMode = OnlineGameMode.FromType(mode, this);
            this.gameModeType = mode;
            if (gameMode == null) throw new Exception($"Invalid game mode {mode}");

            if (owner == null) throw new Exception("No lobby owner");
            NewOwner(owner);

            activateOnAvailable = true;
            if (isOwner)
            {
                this.password = password;
                Available();
            }
            else
            {
                RequestLobby(password);
            }
        }

        public void RequestLobby(string? key)
        {
            RainMeadow.Debug(this);
            if (isPending) throw new InvalidOperationException("pending");
            if (isAvailable) throw new InvalidOperationException("available");
            ClearIncommingBuffers();
            pendingRequest = supervisor.InvokeRPC(RequestedLobby, key).Then(ResolveLobbyRequest);
        }

        [RPCMethod]
        public void RequestedLobby(RPCEvent request, string? key)
        {
            if (this.hasPassword)
            {
                if (this.password != key)
                {
                    request.from.QueueEvent(new GenericResult.Fail(request));
                    return;
                }
            }
            Requested(request);
        }

        public void ResolveLobbyRequest(GenericResult requestResult)
        {
            if (requestResult is GenericResult.Ok)
            {
                MatchmakingManager.instance.JoinLobby(true);
                if (!isAvailable) // this was transfered to me because the previous owner left
                {
                    WaitingForState();
                    if (isOwner)
                    {
                        Available();
                    }
                }
            }
            else if (requestResult is GenericResult.Fail) // I didn't have the right key for this resource
            {
                RainMeadow.Error("locked request for " + this);
                MatchmakingManager.instance.JoinLobby(false);
            }
            else if (requestResult is GenericResult.Error) // I should retry
            {
                Request();
                RainMeadow.Error("request failed for " + this);
            }
        }

        internal override void Tick(uint tick)
        {
            clientSettings = entities.Values.Where(em => em.entity is ClientSettings).ToDictionary(e => e.entity.owner, e=> e.entity as ClientSettings);
            playerAvatars = clientSettings.ToDictionary(e => e.Key, e => e.Value.avatarId);
            gameMode.LobbyTick(tick);
            base.Tick(tick);
        }

        protected override void ActivateImpl()
        {
            if (gameModeType == OnlineGameMode.OnlineGameModeType.ArenaCompetitive) // Arena
            {
                var nr = new Region("arena", 0, -1, null);
                var ns = new WorldSession(nr, this);
                worldSessions.Add(nr.name, ns);
                subresources.Add(ns);
            }
            else // story mode
            {
                foreach (var r in Region.LoadAllRegions(RainMeadow.Ext_SlugcatStatsName.OnlineSessionPlayer))
                {
                    RainMeadow.Debug(r.name);
                    var ws = new WorldSession(r, this);
                    worldSessions.Add(r.name, ws);
                    subresources.Add(ws);
                }
                RainMeadow.Debug(subresources.Count);
            }
        }

        protected override void AvailableImpl()
        {
            
        }

        protected override void DeactivateImpl()
        {
            throw new InvalidOperationException("cant deactivate");
        }

        protected override ResourceState MakeState(uint ts)
        {
            return new LobbyState(this, ts);
        }

        public override string Id()
        {
            return ".";
        }

        public override ushort ShortId()
        {
            throw new NotImplementedException(); // Lobby cannot be a subresource
        }

        public override OnlineResource SubresourceFromShortId(ushort shortId)
        {
            return this.subresources[shortId];
        }

        protected ushort nextId = 1;

        public class LobbyState : ResourceWithSubresourcesState
        {
            [OnlineField]
            public ushort nextId;
            [OnlineField(nullable = true)]
            public Generics.AddRemoveSortedPlayerIDs players;
            [OnlineField(nullable = true)]
            public Generics.AddRemoveSortedUshorts inLobbyIds;
            [OnlineField]
            public string[] mods;
            public LobbyState() : base() { }
            public LobbyState(Lobby lobby, uint ts) : base(lobby, ts)
            {
                nextId = lobby.nextId;
                players = new(lobby.participants.Keys.Select(p => p.id).ToList());
                inLobbyIds = new(lobby.participants.Keys.Select(p => p.inLobbyId).ToList());
                mods = lobby.mods;
            }

            public override void ReadTo(OnlineResource resource)
            {
                var lobby = (Lobby)resource;
                lobby.nextId = nextId;

                for (int i = 0; i < players.list.Count; i++)
                {
                    if (MatchmakingManager.instance.GetPlayer(players.list[i]) is OnlinePlayer p)
                    {
                        if (p.inLobbyId != inLobbyIds.list[i]) RainMeadow.Debug($"Setting player {p} to lobbyId {inLobbyIds.list[i]}");
                        p.inLobbyId = inLobbyIds.list[i];
                    }
                    else
                    {
                        RainMeadow.Error("Player not found! " + players.list[i]);
                    }
                }
                lobby.UpdateParticipants(players.list.Select(MatchmakingManager.instance.GetPlayer).Where(p => p != null).ToList());

                if (!modsChecked)
                {
                    modsChecked = true;
                    RainMeadowModManager.CheckMods(this.mods, lobby.mods);
                }

                base.ReadTo(resource);
            }
        }

        public override string ToString()
        {
            return "Lobby";
        }

        public OnlinePlayer PlayerFromId(ushort id)
        {
            if (id == 0) return null;
            return OnlineManager.players.FirstOrDefault(p => p.inLobbyId == id);
        }

        protected override void NewParticipantImpl(OnlinePlayer player)
        {
            if (isOwner)
            {
                player.inLobbyId = nextId;
                RainMeadow.Debug($"Assigned inLobbyId of {nextId} to player {player}");
                nextId++;
                // todo overflows and repeats (unrealistic but it's a ushort)
            }
            base.NewParticipantImpl(player);
            gameMode.NewPlayerInLobby(player);
        }

        protected override void ParticipantLeftImpl(OnlinePlayer player)
        {
            base.ParticipantLeftImpl(player);
            gameMode.PlayerLeftLobby(player);
        }
    }
}
