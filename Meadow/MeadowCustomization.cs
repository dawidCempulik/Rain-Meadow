﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using RWCustom;

namespace RainMeadow
{
    public partial class MeadowCustomization
    {
        public class CreatureCustomization
        {
            public MeadowProgression.Skin skin;
            private MeadowProgression.SkinData skinData;
            private MeadowProgression.CharacterData characterData;
            public Color tint;
            public float tintAmount;

            public CreatureCustomization(MeadowProgression.Skin skin, Color tint, float tintAmount)
            {
                this.skin = skin;
                this.skinData = MeadowProgression.skinData[skin];
                this.characterData = MeadowProgression.characterData[skinData.character];
                this.tint = new(tint.r, tint.g, tint.b);
                this.tintAmount = tintAmount * skinData.tintFactor;
            }

            internal string EmoteAtlas => skinData.emoteAtlasOverride ?? characterData.emoteAtlas;
            internal string EmotePrefix => skinData.emotePrefixOverride ?? characterData.emotePrefix;
            internal Color EmoteTileColor => Color.Lerp(skinData.emoteTileColorOverride ?? characterData.emoteTileColor, tint, tintAmount);

            internal void ModifyBodyColor(ref Color originalBodyColor)
            {
                if (skinData.statsName != null) originalBodyColor = PlayerGraphics.SlugcatColor(skinData.statsName);
                if (skinData.baseColor.HasValue) originalBodyColor = skinData.baseColor.Value;
                originalBodyColor = Color.Lerp(originalBodyColor, tint, tintAmount);
            }

            internal void ModifyEyeColor(ref Color originalEyeColor)
            {
                if (skinData.eyeColor.HasValue) originalEyeColor = skinData.eyeColor.Value;
            }
        }

        public static ConditionalWeakTable<Creature, CreatureCustomization> creatureCustomizations = new();

        internal static void Customize(Creature creature, OnlineCreature oc)
        {
            if (MeadowAvatarSettings.map.TryGetValue(oc.owner, out MeadowAvatarSettings mas))
            {
                RainMeadow.Debug($"Customizing avatar {creature} for {oc.owner}");
                var mcc = MeadowCustomization.creatureCustomizations.GetValue(creature, (c) => mas.MakeCustomization());
                if (oc.gameModeData is MeadowCreatureData mcd)
                {
                    EmoteDisplayer.map.GetValue(creature, (c) => new EmoteDisplayer(creature, oc, mcd, mcc));
                }
                else
                {
                    RainMeadow.Error("missing mcd?? " + oc);
                }
            }
            else
            {
                RainMeadow.Error("missing mas?? " + oc);
            }

            if(oc.isMine && !oc.isTransferable) // persona, wish there was a better flag
            {
                // playable creatures
                CreatureController.BindCreature(creature);
            }
        }
    }
}
