﻿using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RainMeadow
{
    public class RainMeadowModManager
    {
        public static string[] GetActiveMods()
        {
            return ModManager.ActiveMods.Where(mod => Directory.Exists(Path.Combine(mod.path, "modify", "world"))).ToList().Select(mod => mod.id.ToString()).ToArray();
        }

        internal static void CheckMods(string[] lobbyMods, string[] localMods)
        {
            if (Enumerable.SequenceEqual(localMods, lobbyMods))
            {
                RainMeadow.Debug("Same mod set !");
            }
            else
            {
                RainMeadow.Debug("Mismatching mod set");

                var (MissingMods, ExcessiveMods) = CompareModSets(lobbyMods, localMods);

                bool[] mods = ModManager.InstalledMods.ConvertAll(mod => mod.enabled).ToArray();

                List<int> loadOrder = ModManager.InstalledMods.ConvertAll(mod => mod.loadOrder);

                List<string> unknownMods = new();
                List<ModManager.Mod> modsToEnable = new();
                List<ModManager.Mod> modsToDisable = new();

                foreach (var id in MissingMods)
                {
                    int index = ModManager.InstalledMods.FindIndex(_mod => _mod.id == id);

                    if (index >= 0)
                    {
                        mods[index] = true;
                        modsToEnable.Add(ModManager.InstalledMods[index]);
                    }
                    else
                    {
                        RainMeadow.Debug("Unknown mod: " + id);
                        unknownMods.Add(id);
                    }
                }

                foreach (var id in ExcessiveMods)
                {
                    int index = ModManager.InstalledMods.FindIndex(_mod => _mod.id == id);

                    mods[index] = false;

                    modsToDisable.Add(ModManager.InstalledMods[index]);
                }

                ModApplier modApplyer = new(RWCustom.Custom.rainWorld.processManager, mods.ToList(), loadOrder);

                modApplyer.ShowConfirmation(modsToEnable, modsToDisable, unknownMods);

                modApplyer.OnFinish += (ModApplier modApplyer) =>
                {
                    Utils.Restart($"+connect_lobby {MatchmakingManager.instance.GetLobbyID()}");
                };
            }
        }

        private static (List<string> MissingMods, List<string> ExcessiveMods) CompareModSets(string[] arr1, string[] arr2)
        {
            // Find missing strings in arr2
            var missingStrings = arr1.Except(arr2).ToList();

            // Find excessive strings in arr2
            var excessiveStrings = arr2
                .GroupBy(item => item)
                .Where(group => group.Count() > arr1.Count(item => item == group.Key))
                .Select(group => group.Key)
                .ToList();

            return (missingStrings, excessiveStrings);
        }
    }
}
