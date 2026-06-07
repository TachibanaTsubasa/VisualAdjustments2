using Kingmaker.Blueprints;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.View;
using Kingmaker.Visual.CharacterSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kingmaker.Items;
using Kingmaker.UI.MVVM._VM.ServiceWindows.Inventory;
using Kingmaker.UI.ServiceWindow;
using Owlcat.Runtime.Core.Utils;
using Owlcat.Runtime.UniRx;
using UnityEngine;
using VisualAdjustments2.UI;

namespace VisualAdjustments2.Infrastructure
{
    public static class EeInfraStructure
    {
        public static Dictionary<Color, Texture2D> ColorToTex = new();

        public static int ApplySettings(CharacterSettings settings, Character character)
        {
            if (settings?.EeSettings?.EEs == null || character == null) return 0;

            var changed = 0;
            foreach (var action in settings.EeSettings.EEs.ToList())
            {
#if DEBUG
                Main.DebugLog($"Action of type {action.actionType}, Guid:{action.GUID}");
#endif
                if (action.actionType == EE_Applier.ActionType.Remove)
                {
                    if (RemoveEquipmentEntity(character, action.GUID, action.InternalName))
                        changed++;
                }
                else
                {
                    action.Apply(character);
                }
            }

            return changed;
        }

        public static void DumpRelevantEquipmentState(Character character, CharacterSettings settings, string source)
        {
            try
            {
                if (character == null || settings?.EeSettings?.EEs == null) return;

                var removalNames = settings.EeSettings.EEs
                    .Where(a => a.actionType == EE_Applier.ActionType.Remove)
                    .SelectMany(a =>
                    {
                        var names = new List<string>();
                        if (!string.IsNullOrEmpty(a.InternalName))
                            names.Add(a.InternalName);
                        var loaded = a.Load();
                        if (!string.IsNullOrEmpty(loaded?.name))
                            names.Add(loaded.name);
                        return names;
                    })
                    .Distinct()
                    .ToList();

                var equipment = DescribeEquipmentList(character.EquipmentEntities, removalNames);
                var additional = DescribeEquipmentList(character.AdditionalEquipmentEntities, removalNames);
                var spawned = character.m_EeSpawnedObjects?
                    .Where(a => IsRelevantEquipmentName(a?.ee?.name, removalNames))
                    .Select(a => $"{a.ee.name}")
                    .Distinct()
                    .OrderBy(a => a)
                    .ToList() ?? new List<string>();
                var outfitObjects = character.m_OutfitObjects?
                    .Select(a => new[]
                    {
                        a?.GameObject?.name,
                        a?.OutfitPart?.m_Prefab?.name,
                        a?.OutfitPart?.material?.name
                    })
                    .SelectMany(a => a)
                    .Where(a => IsRelevantEquipmentName(a, removalNames))
                    .Distinct()
                    .OrderBy(a => a)
                    .ToList() ?? new List<string>();
                var renderers = character.m_Renderers?
                    .Select(a => new[]
                    {
                        a?.name,
                        a?.gameObject?.name,
                        a?.sharedMaterial?.name
                    })
                    .SelectMany(a => a)
                    .Where(a => IsRelevantEquipmentName(a, removalNames))
                    .Distinct()
                    .OrderBy(a => a)
                    .ToList() ?? new List<string>();
                var additionalFx = character.m_AdditionalFXs?
                    .Select(a => a?.SpawnedObject?.name)
                    .Where(a => IsRelevantEquipmentName(a, removalNames))
                    .Distinct()
                    .OrderBy(a => a)
                    .ToList() ?? new List<string>();
                var additionalRampTypes = new HashSet<BodyPartType>(
                    character.m_AdditionalVisualSettings?.ColorRamps?
                        .Select(a => a.Type) ?? Enumerable.Empty<BodyPartType>());
                var rampIndices = character.m_RampIndices?
                    .Where(a => a?.EquipmentEntity?.BodyParts?.Any(b => b != null && additionalRampTypes.Contains(b.Type)) == true)
                    .Select(a => $"{a.EquipmentEntity.name}:{a.PrimaryIndex}/{a.SecondaryIndex}/{a.SpecialPrimaryIndex}/{a.SpecialSecondaryIndex}")
                    .Distinct()
                    .OrderBy(a => a)
                    .ToList() ?? new List<string>();

                Main.DebugLog(
                    $"EE dump after {source}: equipment=[{string.Join(", ", equipment)}], additional=[{string.Join(", ", additional)}], spawned=[{string.Join(", ", spawned)}], outfit=[{string.Join(", ", outfitObjects)}], renderers=[{string.Join(", ", renderers)}], additionalFx=[{string.Join(", ", additionalFx)}], additionalRampIndices=[{string.Join(", ", rampIndices)}]");
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
        }

        private static List<string> DescribeEquipmentList(IEnumerable<EquipmentEntity> entities, List<string> removalNames)
        {
            return entities?
                .Where(a => IsRelevantEquipmentName(a?.name, removalNames))
                .Select(a =>
                {
                    var info = a.ToEEInfo();
                    return info != null ? $"{a.name} ({info.Value.GUID})" : a.name;
                })
                .Distinct()
                .OrderBy(a => a)
                .ToList() ?? new List<string>();
        }

        private static bool IsRelevantEquipmentName(string name, List<string> removalNames)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.IndexOf("Demon", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("Mythic", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return removalNames?.Any(a => IsSameEquipmentFamily(name, a)) == true;
        }

        public static EquipmentEntity FindExistingEquipmentEntity(Character character, EquipmentEntity loadedEE, string internalName)
        {
            if (character?.EquipmentEntities == null) return null;

            if (!string.IsNullOrEmpty(internalName))
            {
                var existingByInternalName = character.EquipmentEntities.FirstOrDefault(a => IsSameEquipmentFamily(a?.name, internalName));
                if (existingByInternalName != null) return existingByInternalName;
            }

            if (loadedEE == null) return null;

            return character.EquipmentEntities.FirstOrDefault(a => a == loadedEE || IsSameEquipmentFamily(a?.name, loadedEE.name));
        }

        public static bool IsSameEquipmentFamily(string candidateName, string targetName)
        {
            if (string.IsNullOrEmpty(candidateName) || string.IsNullOrEmpty(targetName)) return false;
            if (candidateName == targetName) return true;

            var familyPrefix = GetEquipmentFamilyPrefix(targetName);
            return !string.IsNullOrEmpty(familyPrefix) &&
                   candidateName.StartsWith(familyPrefix + "_", StringComparison.Ordinal);
        }

        private static string GetEquipmentFamilyPrefix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var genderIndex = name.IndexOf("_M_", StringComparison.Ordinal);
            if (genderIndex < 0)
                genderIndex = name.IndexOf("_F_", StringComparison.Ordinal);
            if (genderIndex > 0)
                return name.Substring(0, genderIndex);

            const string anySuffix = "_Any";
            if (name.EndsWith(anySuffix, StringComparison.Ordinal))
                return name.Substring(0, name.Length - anySuffix.Length);

            return name;
        }

        public static bool RemoveEquipmentEntity(Character character, string guid, string internalName = null, bool logMissing = false)
        {
            var loadedEE = string.IsNullOrEmpty(guid) ? null : ResourcesLibrary.TryGetResource<EquipmentEntity>(guid);
            var existingEE = FindExistingEquipmentEntity(character, loadedEE, internalName);
            var targetName = internalName ?? loadedEE?.name;
            var removed = false;

            var existingMatches = character?.EquipmentEntities?
                .Where(a => IsSameEquipmentFamily(a?.name, targetName ?? loadedEE?.name))
                .Distinct()
                .ToList();

            if (existingMatches?.Count > 0)
            {
                foreach (var ee in existingMatches)
                    character.RemoveEquipmentEntity(ee);
                removed = true;
            }
            else if (existingEE != null)
            {
                character.RemoveEquipmentEntity(existingEE);
                removed = true;
            }

            if (!string.IsNullOrEmpty(targetName) && character.AdditionalEquipmentEntities != null)
            {
                var removedAdditional = character.AdditionalEquipmentEntities.RemoveAll(a => IsSameEquipmentFamily(a?.name, targetName));
                if (removedAdditional > 0)
                {
                    removed = true;
                    character.IsDirty = true;
                    character.IsAtlasesDirty = true;
                    if (logMissing)
                        Main.DebugLog($"Removed {removedAdditional} additional EE(s): {targetName} ({guid})");
                }
            }

            if (RemoveSpawnedEquipmentObjects(character, loadedEE, targetName, guid, logMissing))
                removed = true;

            if (RemoveGeneratedBodyParts(character, loadedEE, targetName, guid, logMissing))
                removed = true;

            if (removed && loadedEE?.BodyParts?.Count > 0)
                character.UpdateCharacter();

            if (!removed && logMissing)
                Main.DebugLog($"Could not find EE to remove. Guid: {guid}, internal name: {internalName}");

            return removed;
        }

        private static bool RemoveSpawnedEquipmentObjects(Character character, EquipmentEntity targetEE, string targetName, string guid, bool logRemoved)
        {
            if (character == null || string.IsNullOrEmpty(targetName)) return false;

            var removed = false;
            var spawnedParts = character.m_EeSpawnedObjects?
                .Where(a => a?.ee != null && IsSameEquipmentFamily(a.ee.name, targetName))
                .ToList();
            var outfitParts = new HashSet<EquipmentEntity.OutfitPart>(
                spawnedParts?.Select(a => a.outfitPart).Where(a => a != null) ?? Enumerable.Empty<EquipmentEntity.OutfitPart>());

            if (targetEE?.OutfitParts != null)
                foreach (var outfitPart in targetEE.OutfitParts.Where(a => a != null))
                    outfitParts.Add(outfitPart);

            var removedOutfitObjects = 0;
            if (outfitParts.Count > 0)
            {
                foreach (var outfitPart in outfitParts)
                {
                    var outfitObjects = character.m_OutfitObjects?
                        .Where(a => a?.OutfitPart == outfitPart)
                        .ToList();

                    if (outfitObjects == null) continue;

                    foreach (var outfitObject in outfitObjects)
                    {
                        if (outfitObject.GameObject != null)
                            UnityEngine.Object.Destroy(outfitObject.GameObject);

                        character.m_OutfitObjects.Remove(outfitObject);
                        removedOutfitObjects++;
                    }
                }

                var removedSpawnedObjects = character.m_EeSpawnedObjects?.RemoveAll(a => a?.ee != null && IsSameEquipmentFamily(a.ee.name, targetName)) ?? 0;
                if (removedOutfitObjects > 0 || removedSpawnedObjects > 0)
                {
                    character.IsDirty = true;
                    character.IsAtlasesDirty = true;
                    removed = true;
                }

                if (removed && logRemoved)
                    Main.DebugLog($"Removed spawned EE objects: {targetName} ({guid}), outfit objects: {removedOutfitObjects}, spawned refs: {removedSpawnedObjects}");
            }

            return removed;
        }

        private static bool RemoveGeneratedBodyParts(Character character, EquipmentEntity targetEE, string targetName, string guid, bool logRemoved)
        {
            if (character == null || targetEE?.BodyParts == null || targetEE.BodyParts.Count == 0) return false;

            var bodyParts = targetEE.BodyParts.Where(a => a != null).ToList();
            if (bodyParts.Count == 0) return false;

            var removed = 0;
            removed += character.m_OverlayBodyParts?.RemoveAll(a => bodyParts.Contains(a)) ?? 0;
            removed += character.m_LastBodyParts?.RemoveAll(a => bodyParts.Contains(a)) ?? 0;
            removed += character.m_NewBodyParts?.RemoveAll(a => bodyParts.Contains(a)) ?? 0;

            if (removed <= 0) return false;

            character.IsDirty = true;
            character.IsAtlasesDirty = true;

            if (logRemoved)
                Main.DebugLog($"Marked body-part EE for rebuild: {targetName} ({guid}), body parts: {bodyParts.Count}, removed cache refs: {removed}");

            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(UnitEntityData), nameof(UnitEntityData.OnViewDidAttach))]
    public static class UnitEntityData_CreateView_Patch
    {
        public static void Postfix(UnitEntityData __instance)
        {
            try
            {
                if (__instance.View?.CharacterAvatar != null && __instance.IsPlayerFaction &&
                    Kingmaker.Game.Instance.Player.AllCharacters.Contains(__instance))
                {
                    var settings = __instance.GetSettings();
                    var hadRemovals = settings?.EeSettings?.EEs?.Any(a => a.actionType == EE_Applier.ActionType.Remove) == true;
                    var changed = EeInfraStructure.ApplySettings(settings, __instance.View.CharacterAvatar);
                    if (changed > 0)
                        __instance.View.CharacterAvatar.UpdateCharacter();
                }
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Character), nameof(Character.ApplyAdditionalVisualSettings))]
    public static class Character_ApplyAdditionalVisualSettings_Patch
    {
        private static bool s_ApplyingVa2EeSettings;
        private static int s_DiagnosticsLogCount;
        private static int s_RenderDiagnosticsLogCount;

        public class FilterState
        {
            public Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings Settings;
            public KingmakerEquipmentEntityReference[] Common;
            public KingmakerEquipmentEntityReference[] InGame;
            public KingmakerEquipmentEntityReference[] DollRoom;
            public Kingmaker.ResourceLinks.PrefabLink[] CommonFXs;
            public Kingmaker.ResourceLinks.PrefabLink[] InGameFXs;
            public Kingmaker.ResourceLinks.PrefabLink[] DollRoomFXs;
            public Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp[] ColorRamps;
            public int Removed;
            public int RemovedFXs;
            public int RemovedColorRamps;
        }

        internal static UnitEntityData FindUnitForCharacter(Character character)
        {
            if (character == null) return null;

            var dollRoom = Game.Instance?.UI?.Common?.DollRoom;
            if (dollRoom?.m_Avatar == character || dollRoom?.GetAvatar() == character)
                return dollRoom.Unit;

            return Game.Instance?.Player?.AllCharacters?
                .FirstOrDefault(a => a?.View?.CharacterAvatar == character);
        }

        internal static void ApplySavedSettings(Character character, UnitEntityData unit, string source)
        {
            if (character == null || unit?.IsPlayerFaction != true) return;

            var settings = unit.GetSettings();
            var removals = settings?.EeSettings?.EEs?
                .Where(a => a.actionType == EE_Applier.ActionType.Remove)
                .ToList();
            if (removals?.Count > 0 != true) return;

            s_ApplyingVa2EeSettings = true;
            var removedAdditionalVisualColorRamps = GetRemovedAdditionalVisualColorRamps(character, removals);
            var changed = EeInfraStructure.ApplySettings(settings, character);
            var filteredAdditionalVisualSettings = ApplyFilteredAdditionalVisualSettings(
                character,
                removals,
                removedAdditionalVisualColorRamps,
                source,
                unit.CharacterName);
            var clearedRamps = ClearAdditionalVisualRampIndices(
                character,
                removedAdditionalVisualColorRamps,
                source,
                unit.CharacterName);
            var clearedDollRamps = ClearAdditionalVisualDollDataRampIndices(
                unit,
                removedAdditionalVisualColorRamps,
                source,
                unit.CharacterName);
            var clearedSerializedDollRamps = ClearAdditionalVisualSerializedDollRampIndices(
                character,
                settings,
                removedAdditionalVisualColorRamps,
                source,
                unit.CharacterName);
            if (clearedSerializedDollRamps > 0)
                DollVM.RefreshCurrentDoll(unit, source);
            LogApplyDiagnostics(
                character,
                unit,
                settings,
                source,
                changed,
                clearedRamps,
                clearedDollRamps,
                clearedSerializedDollRamps);
            if (changed > 0 || filteredAdditionalVisualSettings || clearedRamps > 0 || clearedDollRamps > 0 || clearedSerializedDollRamps > 0)
                ForceVisualRebuildAfterRemovals(character, source, unit.CharacterName);
            EeInfraStructure.DumpRelevantEquipmentState(character, settings, source);
            Main.DebugLog($"Applied saved EE removals after {source}: {unit.CharacterName}, count: {settings.EeSettings.EEs.Count}");
        }

        private static bool ShouldLogDiagnostics()
        {
            return s_DiagnosticsLogCount++ < 120;
        }

        private static void LogApplyDiagnostics(
            Character character,
            UnitEntityData unit,
            CharacterSettings settings,
            string source,
            int changed,
            int clearedRamps,
            int clearedDollRamps,
            int clearedSerializedDollRamps)
        {
            try
            {
                if (!ShouldLogDiagnostics()) return;

                var dollRoom = Game.Instance?.UI?.Common?.DollRoom;
                Main.DebugLog(
                    $"VA2 EE apply diag after {source}: unit={unit?.CharacterName}, avatar={DescribeAvatarKind(character, unit)}, changed={changed}, clearedRamps={clearedRamps}, clearedDollData={clearedDollRamps}, clearedSerialized={clearedSerializedDollRamps}, activeDollVM={DollVM.HasActiveFor(unit)}, doll={DescribeSerializedDoll(settings?.doll)}");
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
        }

        internal static void ForceVisualRebuildAfterRemovals(Character character, string source, string characterName)
        {
            if (character == null) return;

            var unit = FindUnitForCharacter(character);
            LogVisualRenderState(character, unit, source, "before rebuild");

            RefreshClassEquipmentForVisualRebuild(character, unit, source);
            character.ClearAtlases();
            character.IsDirty = true;
            character.IsAtlasesDirty = true;
            character.ForceDoUpdate();

            RefreshDollRoomForVisualRebuild(character, unit, source);
            ScheduleDollRoomAtlasFollowUp(character, unit, source);

            LogVisualRenderState(character, unit, source, "after rebuild");
            Main.DebugLog($"Refreshed visual state after {source}: {characterName}");
        }

        private static void RefreshClassEquipmentForVisualRebuild(Character character, UnitEntityData unit, string source)
        {
            try
            {
                if (unit?.View == null) return;

                unit.View.UpdateClassEquipment();

                var dollRoom = Game.Instance?.UI?.Common?.DollRoom;
                if (dollRoom?.m_Avatar == character || dollRoom?.GetAvatar() == character)
                    character.RebuildOutfit();

                Main.DebugLog($"Refreshed class equipment before visual rebuild after {source}: {unit.CharacterName}, avatar={DescribeAvatarKind(character, unit)}");
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
        }

        private static void RefreshDollRoomForVisualRebuild(Character character, UnitEntityData unit, string source)
        {
            try
            {
                var dollRoom = Game.Instance?.UI?.Common?.DollRoom;
                if (dollRoom == null || (dollRoom.m_Avatar != character && dollRoom.GetAvatar() != character)) return;

                dollRoom.UpdateCharacter();
                Main.DebugLog($"Refreshed DollRoom character after visual rebuild after {source}: {unit?.CharacterName}");
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
        }

        private static void ScheduleDollRoomAtlasFollowUp(Character character, UnitEntityData unit, string source)
        {
            try
            {
                var dollRoom = Game.Instance?.UI?.Common?.DollRoom;
                if (dollRoom == null || (dollRoom.m_Avatar != character && dollRoom.GetAvatar() != character)) return;

                var characterHash = ObjectHash(character);
                foreach (var frameDelay in new[] { 1, 3, 6 })
                {
                    DelayedInvoker.InvokeInFrames(() =>
                    {
                        try
                        {
                            var currentDollRoom = Game.Instance?.UI?.Common?.DollRoom;
                            var currentAvatar = currentDollRoom?.m_Avatar;
                            if (currentAvatar == null || ObjectHash(currentAvatar) != characterHash) return;

                            currentAvatar.ForceDoUpdate();
                            currentDollRoom.UpdateCharacter();
                            Main.DebugLog($"VA2 delayed atlas follow-up after {source}: frame={frameDelay}, unit={unit?.CharacterName}, dirty={currentAvatar.IsDirty}, atlasesDirty={currentAvatar.IsAtlasesDirty}");
                            LogVisualRenderState(currentAvatar, unit, source, $"delayed frame {frameDelay}");
                        }
                        catch (Exception e)
                        {
                            Main.Logger.Error(e.ToString());
                        }
                    }, frameDelay);
                }
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
        }

        internal static void LogVisualRenderState(Character character, UnitEntityData unit, string source, string phase)
        {
            try
            {
                if (character == null || s_RenderDiagnosticsLogCount++ >= 80) return;

                var dollRoom = Game.Instance?.UI?.Common?.DollRoom;
                var sceneAvatar = unit?.View?.CharacterAvatar;
                var dollAvatar = dollRoom?.m_Avatar;
                var getAvatar = dollRoom?.GetAvatar();

                Main.DebugLog(
                    $"VA2 render diag {phase} {source}: unit={unit?.CharacterName}, avatar={DescribeAvatarKind(character, unit)}, charHash={ObjectHash(character)}, sceneHash={ObjectHash(sceneAvatar)}, dollHash={ObjectHash(dollAvatar)}, getAvatarHash={ObjectHash(getAvatar)}, dirty={character.IsDirty}, atlasesDirty={character.IsAtlasesDirty}, renderers={character.m_Renderers?.Count ?? 0}, rampCount={character.m_RampIndices?.Count ?? 0}, addRampCount={character.m_AdditionalVisualSettings?.ColorRamps?.Length ?? 0}, equipment={character.EquipmentEntities?.Count ?? 0}, additional={character.AdditionalEquipmentEntities?.Count ?? 0}, spawned={character.m_EeSpawnedObjects?.Count ?? 0}, outfit={character.m_OutfitObjects?.Count ?? 0}, ramps=[{DescribeAllRampIndices(character)}], addRamps=[{DescribeAdditionalVisualRamps(character)}], materials=[{DescribeRendererMaterials(character)}]");
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
        }

        private static string DescribeAvatarKind(Character character, UnitEntityData unit)
        {
            var dollRoom = Game.Instance?.UI?.Common?.DollRoom;
            return dollRoom?.m_Avatar == character || dollRoom?.GetAvatar() == character
                ? "dollroom"
                : unit?.View?.CharacterAvatar == character
                    ? "scene"
                    : "other";
        }

        private static int ObjectHash(object value)
        {
            return value == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }

        private static string DescribeAllRampIndices(Character character)
        {
            return string.Join(", ", character.m_RampIndices?
                .Take(24)
                .Select(a => $"{ShortName(a?.EquipmentEntity?.name)}:{a?.PrimaryIndex}/{a?.SecondaryIndex}/{a?.SpecialPrimaryIndex}/{a?.SpecialSecondaryIndex}") ?? Enumerable.Empty<string>());
        }

        private static string DescribeAdditionalVisualRamps(Character character)
        {
            return string.Join(", ", character.m_AdditionalVisualSettings?.ColorRamps?
                .Take(24)
                .Select(a => $"{a.Type}:{a.Primary}/{a.Secondary}/{a.SpecialPrimary}/{a.SpecialSecondary}") ?? Enumerable.Empty<string>());
        }

        private static string DescribeRendererMaterials(Character character)
        {
            return string.Join(", ", character.m_Renderers?
                .Where(a => a != null)
                .Take(16)
                .Select(DescribeRendererMaterial) ?? Enumerable.Empty<string>());
        }

        private static string DescribeRendererMaterial(Renderer renderer)
        {
            try
            {
                var material = renderer.sharedMaterial;
                return $"{ShortName(renderer.name)}|go={ShortName(renderer.gameObject?.name)}|mat={ShortName(material?.name)}|shader={ShortName(material?.shader?.name)}|tex={ShortName(material?.mainTexture?.name)}";
            }
            catch (Exception e)
            {
                return $"<renderer error: {e.Message}>";
            }
        }

        private static string ShortName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= 42 ? value : value.Substring(0, 42);
        }

        private static bool ApplyFilteredAdditionalVisualSettings(
            Character character,
            List<EE_Applier> removals,
            Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp[] removedColorRamps,
            string source,
            string characterName)
        {
            try
            {
                var current = character?.m_AdditionalVisualSettings;
                if (current == null || removals?.Count > 0 != true) return false;

                var filtered = new Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings
                {
                    m_Conditions = current.m_Conditions,
                    OverrideFootprintType = current.OverrideFootprintType,
                    FootprintType = current.FootprintType
                };

                var removedProviders = 0;
                var removedFx = 0;
                filtered.CommonSettings = CloneFilteredSettingsData(current.CommonSettings, character, removals, out var commonRemoved, out var commonFx);
                removedProviders += commonRemoved;
                removedFx += commonFx;
                filtered.InGameSettings = CloneFilteredSettingsData(current.InGameSettings, character, removals, out var inGameRemoved, out var inGameFx);
                removedProviders += inGameRemoved;
                removedFx += inGameFx;
                filtered.DollRoomSettings = CloneFilteredSettingsData(current.DollRoomSettings, character, removals, out var dollRoomRemoved, out var dollRoomFx);
                removedProviders += dollRoomRemoved;
                removedFx += dollRoomFx;

                if (removedProviders <= 0) return false;

                var originalRampCount = current.ColorRamps?.Length ?? 0;
                filtered.ColorRamps = removedColorRamps?.Length > 0
                    ? Array.Empty<Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp>()
                    : current.ColorRamps;

                character.SetAdditionalVisualSettings(filtered);
                character.IsAtlasesDirty = true;
                Main.DebugLog(
                    $"Applied filtered additional visual settings after {source}: {characterName}, providers={removedProviders}, fx={removedFx}, colorRamps={originalRampCount}->{filtered.ColorRamps?.Length ?? 0}");
                return true;
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
                return false;
            }
        }

        internal static Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings PrepareFilteredAdditionalVisualSettings(
            UnitEntityData unit,
            Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings additionalVisualSettings,
            string source)
        {
            try
            {
                if (unit?.IsPlayerFaction != true) return additionalVisualSettings;

                var settings = unit.GetSettings();
                var removals = settings?.EeSettings?.EEs?
                    .Where(a => a.actionType == EE_Applier.ActionType.Remove)
                    .ToList();
                if (removals?.Count > 0 != true) return additionalVisualSettings;

                var resolvedSettings = additionalVisualSettings;
                if (resolvedSettings == null)
                {
                    var visualSettingsProvider = unit.Progression?.GetVisualSettingsProvider();
                    resolvedSettings = visualSettingsProvider != null
                        ? visualSettingsProvider.CharacterClass.GetAdditionalVisualSettings(visualSettingsProvider.Level)
                        : null;
                }

                var character = unit.View?.CharacterAvatar;
                var removedColorRamps = GetRemovedAdditionalVisualColorRamps(resolvedSettings, character, removals);
                var filtered = BuildFilteredAdditionalVisualSettings(
                    resolvedSettings,
                    character,
                    removals,
                    removedColorRamps,
                    source,
                    unit.CharacterName,
                    out var removedProviders,
                    out var removedFx,
                    out var originalRampCount);

                if (filtered == null) return resolvedSettings;

                Main.DebugLog(
                    $"Prepared filtered additional visual settings before {source}: {unit.CharacterName}, providers={removedProviders}, fx={removedFx}, colorRamps={originalRampCount}->{filtered.ColorRamps?.Length ?? 0}");
                return filtered;
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
                return additionalVisualSettings;
            }
        }

        private static Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings BuildFilteredAdditionalVisualSettings(
            Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings current,
            Character character,
            List<EE_Applier> removals,
            Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp[] removedColorRamps,
            string source,
            string characterName,
            out int removedProviders,
            out int removedFx,
            out int originalRampCount)
        {
            removedProviders = 0;
            removedFx = 0;
            originalRampCount = current?.ColorRamps?.Length ?? 0;
            if (current == null || character == null || removals?.Count > 0 != true) return null;

            var filtered = new Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings
            {
                m_Conditions = current.m_Conditions,
                OverrideFootprintType = current.OverrideFootprintType,
                FootprintType = current.FootprintType
            };

            filtered.CommonSettings = CloneFilteredSettingsData(current.CommonSettings, character, removals, out var commonRemoved, out var commonFx);
            removedProviders += commonRemoved;
            removedFx += commonFx;
            filtered.InGameSettings = CloneFilteredSettingsData(current.InGameSettings, character, removals, out var inGameRemoved, out var inGameFx);
            removedProviders += inGameRemoved;
            removedFx += inGameFx;
            filtered.DollRoomSettings = CloneFilteredSettingsData(current.DollRoomSettings, character, removals, out var dollRoomRemoved, out var dollRoomFx);
            removedProviders += dollRoomRemoved;
            removedFx += dollRoomFx;

            if (removedProviders <= 0) return null;

            filtered.ColorRamps = removedColorRamps?.Length > 0
                ? Array.Empty<Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp>()
                : current.ColorRamps;

            return filtered;
        }

        private static Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp[] GetRemovedAdditionalVisualColorRamps(
            Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings additionalSettings,
            Character character,
            List<EE_Applier> removals)
        {
            var colorRamps = additionalSettings?.ColorRamps;
            if (colorRamps == null || colorRamps.Length == 0 || character == null || removals?.Count > 0 != true)
                return Array.Empty<Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp>();

            var hasRemovedProvider =
                SettingsContainRemovedEquipment(additionalSettings.CommonSettings, character, removals) ||
                SettingsContainRemovedEquipment(additionalSettings.InGameSettings, character, removals) ||
                SettingsContainRemovedEquipment(additionalSettings.DollRoomSettings, character, removals);

            return hasRemovedProvider ? colorRamps : Array.Empty<Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp>();
        }

        private static Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.SettingsData CloneFilteredSettingsData(
            Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.SettingsData source,
            Character character,
            List<EE_Applier> removals,
            out int removedProviders,
            out int removedFxCount)
        {
            removedProviders = 0;
            removedFxCount = 0;
            if (source == null) return null;

            var original = source.m_EquipmentEntities;
            var filtered = original?
                .Where(a => !MatchesRemovedEquipmentEntity(a, character, removals))
                .ToArray();

            if (original != null)
                removedProviders = original.Length - (filtered?.Length ?? 0);

            var fxs = source.FXs;
            if (removedProviders > 0 && fxs?.Length > 0)
            {
                removedFxCount = fxs.Length;
                fxs = Array.Empty<Kingmaker.ResourceLinks.PrefabLink>();
            }

            if (removedProviders > 0 && original != null)
            {
                foreach (var removed in original.Except(filtered ?? Array.Empty<KingmakerEquipmentEntityReference>()))
                    Main.DebugLog($"Filtered persistent additional visual provider: {DescribeKee(removed, character)}");
            }

            return new Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.SettingsData
            {
                m_EquipmentEntities = filtered,
                FXs = fxs
            };
        }

        internal static int ClearAdditionalVisualRampIndices(
            Character character,
            List<EE_Applier> removals,
            string source,
            string characterName)
        {
            try
            {
                var colorRamps = GetRemovedAdditionalVisualColorRamps(character, removals);
                if (character?.m_RampIndices == null || colorRamps == null || colorRamps.Length == 0 || removals?.Count > 0 != true)
                    return 0;

                var removed = character.m_RampIndices.RemoveAll(a => MatchesAdditionalVisualRamp(a, colorRamps));

                if (removed > 0)
                {
                    character.IsDirty = true;
                    character.IsAtlasesDirty = true;
                    Main.DebugLog($"Cleared {removed} additional visual ramp index(es) after {source}: {characterName}");
                }

                return removed;
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
                return 0;
            }
        }

        private static int ClearAdditionalVisualRampIndices(
            Character character,
            Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp[] colorRamps,
            string source,
            string characterName)
        {
            try
            {
                if (character?.m_RampIndices == null || colorRamps == null || colorRamps.Length == 0)
                    return 0;

                var removed = character.m_RampIndices.RemoveAll(a => MatchesAdditionalVisualRamp(a, colorRamps));

                if (removed > 0)
                {
                    character.IsDirty = true;
                    character.IsAtlasesDirty = true;
                    Main.DebugLog($"Cleared {removed} filtered additional visual ramp index(es) after {source}: {characterName}");
                }

                return removed;
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
                return 0;
            }
        }

        internal static int ClearAdditionalVisualDollDataRampIndices(
            Character character,
            UnitEntityData unit,
            List<EE_Applier> removals,
            string source,
            string characterName)
        {
            try
            {
                var colorRamps = GetRemovedAdditionalVisualColorRamps(character, removals);
                var dollPart = unit?.Get<UnitPartDollData>();
                if (dollPart == null || colorRamps == null || colorRamps.Length == 0) return 0;

                var removed = 0;
                var seen = new HashSet<DollData>();
                foreach (var dollData in GetDollDatas(dollPart))
                {
                    if (dollData == null || !seen.Add(dollData)) continue;

                    removed += RemoveDollRampEntries(dollData.EntityRampIdices, colorRamps, true);
                    removed += RemoveDollRampEntries(dollData.EntitySecondaryRampIdices, colorRamps, false);
                }

                if (removed > 0)
                    Main.DebugLog($"Cleared {removed} additional visual doll-data ramp index(es) after {source}: {characterName}");

                return removed;
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
                return 0;
            }
        }

        private static int ClearAdditionalVisualDollDataRampIndices(
            UnitEntityData unit,
            Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp[] colorRamps,
            string source,
            string characterName)
        {
            try
            {
                var dollPart = unit?.Get<UnitPartDollData>();
                if (dollPart == null || colorRamps == null || colorRamps.Length == 0) return 0;

                var removed = 0;
                var seen = new HashSet<DollData>();
                foreach (var dollData in GetDollDatas(dollPart))
                {
                    if (dollData == null || !seen.Add(dollData)) continue;

                    removed += RemoveDollRampEntries(dollData.EntityRampIdices, colorRamps, true);
                    removed += RemoveDollRampEntries(dollData.EntitySecondaryRampIdices, colorRamps, false);
                }

                if (removed > 0)
                    Main.DebugLog($"Cleared {removed} filtered additional visual doll-data ramp index(es) after {source}: {characterName}");

                return removed;
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
                return 0;
            }
        }

        internal static int ClearAdditionalVisualSerializedDollRampIndices(
            Character character,
            CharacterSettings settings,
            List<EE_Applier> removals,
            string source,
            string characterName)
        {
            try
            {
                var colorRamps = GetRemovedAdditionalVisualColorRamps(character, removals);
                var doll = settings?.doll;
                if (doll == null || colorRamps == null || colorRamps.Length == 0)
                {
                    if (ShouldLogDiagnostics())
                        Main.DebugLog($"VA2 serialized doll clear skipped after {source}: {characterName}, hasDoll={doll != null}, colorRampCount={colorRamps?.Length ?? 0}, avatar={(Game.Instance?.UI?.Common?.DollRoom?.m_Avatar == character ? "dollroom" : "non-dollroom")}");
                    return 0;
                }

                var removed = 0;
                var fields = new List<string>();

                if (ClearSerializedRampByGuid(doll.HairID, ref doll.HairRampIndex, colorRamps, true))
                {
                    removed++;
                    fields.Add("HairRampIndex");
                }

                if (ClearSerializedSkinRamp(doll, colorRamps))
                {
                    removed++;
                    fields.Add("SkinRampIndex");
                }

                if (ClearSerializedRampByGuid(doll.HornID, ref doll.HornsRampIndex, colorRamps, true))
                {
                    removed++;
                    fields.Add("HornsRampIndex");
                }

                if (ClearSerializedRampByGuids(doll.ClothesIDs, ref doll.EquipmentRampIndex, colorRamps, true))
                {
                    removed++;
                    fields.Add("EquipmentRampIndex");
                }

                if (ClearSerializedRampByGuids(doll.ClothesIDs, ref doll.EquipmentRampIndexSecondary, colorRamps, false))
                {
                    removed++;
                    fields.Add("EquipmentRampIndexSecondary");
                }

                if (removed > 0)
                    Main.DebugLog($"Cleared {removed} serialized doll ramp field(s) after {source}: {characterName}, fields=[{string.Join(", ", fields)}]");
                else if (ShouldLogDiagnostics())
                    Main.DebugLog($"VA2 serialized doll clear found no matching fields after {source}: {characterName}, colorRampTypes=[{string.Join(", ", colorRamps.Select(a => $"{a.Type}:{a.Primary}/{a.Secondary}/{a.SpecialPrimary}/{a.SpecialSecondary}"))}], doll={DescribeSerializedDoll(doll)}");

                return removed;
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
                return 0;
            }
        }

        private static int ClearAdditionalVisualSerializedDollRampIndices(
            Character character,
            CharacterSettings settings,
            Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp[] colorRamps,
            string source,
            string characterName)
        {
            try
            {
                var doll = settings?.doll;
                if (doll == null || colorRamps == null || colorRamps.Length == 0)
                {
                    if (ShouldLogDiagnostics())
                        Main.DebugLog($"VA2 serialized doll clear skipped after {source}: {characterName}, hasDoll={doll != null}, colorRampCount={colorRamps?.Length ?? 0}, avatar={(Game.Instance?.UI?.Common?.DollRoom?.m_Avatar == character ? "dollroom" : "non-dollroom")}");
                    return 0;
                }

                var removed = 0;
                var fields = new List<string>();

                if (ClearSerializedRampByGuid(doll.HairID, ref doll.HairRampIndex, colorRamps, true))
                {
                    removed++;
                    fields.Add("HairRampIndex");
                }

                if (ClearSerializedSkinRamp(doll, colorRamps))
                {
                    removed++;
                    fields.Add("SkinRampIndex");
                }

                if (ClearSerializedRampByGuid(doll.HornID, ref doll.HornsRampIndex, colorRamps, true))
                {
                    removed++;
                    fields.Add("HornsRampIndex");
                }

                if (ClearSerializedRampByGuids(doll.ClothesIDs, ref doll.EquipmentRampIndex, colorRamps, true))
                {
                    removed++;
                    fields.Add("EquipmentRampIndex");
                }

                if (ClearSerializedRampByGuids(doll.ClothesIDs, ref doll.EquipmentRampIndexSecondary, colorRamps, false))
                {
                    removed++;
                    fields.Add("EquipmentRampIndexSecondary");
                }

                if (removed > 0)
                    Main.DebugLog($"Cleared {removed} filtered serialized doll ramp field(s) after {source}: {characterName}, fields=[{string.Join(", ", fields)}]");
                else if (ShouldLogDiagnostics())
                    Main.DebugLog($"VA2 serialized doll clear found no matching fields after {source}: {characterName}, colorRampTypes=[{string.Join(", ", colorRamps.Select(a => $"{a.Type}:{a.Primary}/{a.Secondary}/{a.SpecialPrimary}/{a.SpecialSecondary}"))}], doll={DescribeSerializedDoll(doll)}");

                return removed;
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
                return 0;
            }
        }

        internal static string DescribeSerializedDoll(SerializedDollState doll)
        {
            if (doll == null) return "null";
            return $"hair={ShortGuid(doll.HairID)}:{doll.HairRampIndex}, skin={doll.SkinRampIndex}, head={ShortGuid(doll.HeadAssetID)}, brows={ShortGuid(doll.EyebrowsID)}, horn={ShortGuid(doll.HornID)}:{doll.HornsRampIndex}, equip={doll.EquipmentRampIndex}/{doll.EquipmentRampIndexSecondary}, clothes=[{string.Join(",", doll.ClothesIDs?.Select(ShortGuid) ?? Enumerable.Empty<string>())}]";
        }

        private static string ShortGuid(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= 8 ? value : value.Substring(0, 8);
        }

        private static IEnumerable<DollData> GetDollDatas(UnitPartDollData dollPart)
        {
            if (dollPart?.Default != null)
                yield return dollPart.Default;
            if (dollPart?.ActiveDoll != null)
                yield return dollPart.ActiveDoll;
            if (dollPart?.m_Special != null)
            {
                foreach (var dollData in dollPart.m_Special.Values)
                    if (dollData != null)
                        yield return dollData;
            }
        }

        private static int RemoveDollRampEntries(Dictionary<string, int> rampIndices, Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp[] colorRamps, bool primary)
        {
            if (rampIndices == null || rampIndices.Count == 0) return 0;

            var removeKeys = rampIndices
                .Where(a => EquipmentEntityMatchesAdditionalVisualRamp(a.Key, a.Value, colorRamps, primary))
                .Select(a => a.Key)
                .ToList();

            foreach (var key in removeKeys)
                rampIndices.Remove(key);

            return removeKeys.Count;
        }

        private static bool ClearSerializedSkinRamp(SerializedDollState doll, Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp[] colorRamps)
        {
            if (doll == null || doll.SkinRampIndex == -1) return false;

            var state = doll.GetDollState();
            var matches = state?.GetSkinEntities()?
                .Select(a => a.Load())
                .Where(a => a != null)
                .Any(a => EquipmentEntityMatchesAdditionalVisualRamp(a, doll.SkinRampIndex, colorRamps, true)) == true;

            if (!matches) return false;

            doll.SkinRampIndex = -1;
            return true;
        }

        private static bool ClearSerializedRampByGuids(IEnumerable<string> guids, ref int index, Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp[] colorRamps, bool primary)
        {
            if (index == -1 || guids == null) return false;

            foreach (var guid in guids)
            {
                if (EquipmentEntityMatchesAdditionalVisualRamp(guid, index, colorRamps, primary))
                {
                    index = -1;
                    return true;
                }
            }

            return false;
        }

        private static bool ClearSerializedRampByGuid(string guid, ref int index, Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp[] colorRamps, bool primary)
        {
            if (index == -1 || string.IsNullOrEmpty(guid)) return false;
            if (!EquipmentEntityMatchesAdditionalVisualRamp(guid, index, colorRamps, primary)) return false;

            index = -1;
            return true;
        }

        private static bool EquipmentEntityMatchesAdditionalVisualRamp(string guid, int index, Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp[] colorRamps, bool primary)
        {
            if (string.IsNullOrEmpty(guid)) return false;

            var ee = ResourcesLibrary.TryGetResource<EquipmentEntity>(guid);
            return EquipmentEntityMatchesAdditionalVisualRamp(ee, index, colorRamps, primary);
        }

        private static bool EquipmentEntityMatchesAdditionalVisualRamp(EquipmentEntity ee, int index, Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp[] colorRamps, bool primary)
        {
            if (ee?.BodyParts == null || colorRamps == null) return false;

            foreach (var bodyPart in ee.BodyParts.Where(a => a != null))
            {
                foreach (var colorRamp in colorRamps.Where(a => a.Type == bodyPart.Type))
                {
                    if (primary && colorRamp.UsePrimary && index == colorRamp.Primary) return true;
                    if (!primary && colorRamp.UseSecondary && index == colorRamp.Secondary) return true;
                }
            }

            return false;
        }

        private static bool MatchesAdditionalVisualRamp(Character.SelectedRampIndices indices, Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp[] colorRamps)
        {
            var ee = indices?.EquipmentEntity;
            if (ee?.BodyParts == null || colorRamps == null) return false;

            foreach (var bodyPart in ee.BodyParts.Where(a => a != null))
            {
                foreach (var colorRamp in colorRamps.Where(a => a.Type == bodyPart.Type))
                {
                    if (colorRamp.UsePrimary && indices.PrimaryIndex == colorRamp.Primary) return true;
                    if (colorRamp.UseSecondary && indices.SecondaryIndex == colorRamp.Secondary) return true;
                    if (colorRamp.UseSpecialPrimary && indices.SpecialPrimaryIndex == colorRamp.SpecialPrimary) return true;
                    if (colorRamp.UseSpecialSecondary && indices.SpecialSecondaryIndex == colorRamp.SpecialSecondary) return true;
                }
            }

            return false;
        }

        private static Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp[] GetRemovedAdditionalVisualColorRamps(Character character, List<EE_Applier> removals)
        {
            var additionalSettings = character?.m_AdditionalVisualSettings;
            var colorRamps = additionalSettings?.ColorRamps;
            if (colorRamps == null || colorRamps.Length == 0 || removals?.Count > 0 != true)
                return Array.Empty<Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp>();

            var hasRemovedProvider =
                SettingsContainRemovedEquipment(additionalSettings.CommonSettings, character, removals) ||
                SettingsContainRemovedEquipment(additionalSettings.InGameSettings, character, removals) ||
                SettingsContainRemovedEquipment(additionalSettings.DollRoomSettings, character, removals);

            return hasRemovedProvider ? colorRamps : Array.Empty<Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp>();
        }

        internal static void ClearGuard()
        {
            s_ApplyingVa2EeSettings = false;
        }

        public static void Prefix(Character __instance, ref FilterState __state)
        {
            try
            {
                var unit = FindUnitForCharacter(__instance);
                var settings = unit?.GetSettings();
                var removals = settings?.EeSettings?.EEs?
                    .Where(a => a.actionType == EE_Applier.ActionType.Remove)
                    .ToList();
                if (unit?.IsPlayerFaction != true || removals?.Count > 0 != true) return;

                var additionalSettings = __instance?.m_AdditionalVisualSettings;
                if (additionalSettings == null) return;

                __state = new FilterState
                {
                    Settings = additionalSettings,
                    Common = additionalSettings.CommonSettings?.m_EquipmentEntities,
                    InGame = additionalSettings.InGameSettings?.m_EquipmentEntities,
                    DollRoom = additionalSettings.DollRoomSettings?.m_EquipmentEntities,
                    CommonFXs = additionalSettings.CommonSettings?.FXs,
                    InGameFXs = additionalSettings.InGameSettings?.FXs,
                    DollRoomFXs = additionalSettings.DollRoomSettings?.FXs,
                    ColorRamps = additionalSettings.ColorRamps
                };

                __state.Removed += FilterAdditionalVisualSettings(additionalSettings.CommonSettings, __instance, removals, out var commonFx);
                __state.RemovedFXs += commonFx;
                __state.Removed += FilterAdditionalVisualSettings(additionalSettings.InGameSettings, __instance, removals, out var inGameFx);
                __state.RemovedFXs += inGameFx;
                __state.Removed += FilterAdditionalVisualSettings(additionalSettings.DollRoomSettings, __instance, removals, out var dollRoomFx);
                __state.RemovedFXs += dollRoomFx;

                if (__state.Removed > 0 && additionalSettings.ColorRamps?.Length > 0)
                {
                    __state.RemovedColorRamps = additionalSettings.ColorRamps.Length;
                    additionalSettings.ColorRamps = Array.Empty<Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.ColorRamp>();
                    Main.DebugLog($"Filtered {__state.RemovedColorRamps} additional visual color ramp(s) paired with removed EE provider(s)");
                }

                if (__state.Removed > 0)
                    Main.DebugLog($"Filtered {__state.Removed} additional visual EE provider(s), {__state.RemovedFXs} FX provider(s), and {__state.RemovedColorRamps} color ramp(s) before ApplyAdditionalVisualSettings: {unit.CharacterName}");
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
        }

        private static int FilterAdditionalVisualSettings(
            Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.SettingsData settingsData,
            Character character,
            List<EE_Applier> removals,
            out int removedFxCount)
        {
            removedFxCount = 0;
            var original = settingsData?.m_EquipmentEntities;
            if (original == null || original.Length == 0) return 0;

            var filtered = original
                .Where(a => !MatchesRemovedEquipmentEntity(a, character, removals))
                .ToArray();

            if (filtered.Length == original.Length) return 0;

            foreach (var removed in original.Except(filtered))
                Main.DebugLog($"Filtered additional visual provider loading: {DescribeKee(removed, character)}");

            removedFxCount = settingsData.FXs?.Length ?? 0;
            if (removedFxCount > 0)
            {
                settingsData.FXs = Array.Empty<Kingmaker.ResourceLinks.PrefabLink>();
                Main.DebugLog($"Filtered {removedFxCount} additional visual FX provider(s) paired with removed EE provider(s)");
            }

            settingsData.m_EquipmentEntities = filtered;
            return original.Length - filtered.Length;
        }

        private static bool SettingsContainRemovedEquipment(
            Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings.SettingsData settingsData,
            Character character,
            List<EE_Applier> removals)
        {
            return settingsData?.m_EquipmentEntities?
                .Any(a => MatchesRemovedEquipmentEntity(a, character, removals)) == true;
        }

        private static string DescribeKee(KingmakerEquipmentEntityReference reference, Character character)
        {
            try
            {
                var kee = reference?.Get();
                if (kee == null) return "<null>";

                var loadedNames = kee.Load(character.m_RuntimeGender, character.m_RuntimeRace)?
                    .Where(a => a != null)
                    .Select(a =>
                    {
                        var info = a.ToEEInfo();
                        return info != null ? $"{a.name} ({info.Value.GUID})" : a.name;
                    })
                    .ToList() ?? new List<string>();

                return string.Join(", ", loadedNames);
            }
            catch (Exception e)
            {
                return $"<error: {e.Message}>";
            }
        }

        private static bool MatchesRemovedEquipmentEntity(
            KingmakerEquipmentEntityReference reference,
            Character character,
            List<EE_Applier> removals)
        {
            var kee = reference?.Get();
            if (kee == null || removals == null) return false;

            var removeGuids = new HashSet<string>(removals.Select(a => a.GUID).Where(a => !string.IsNullOrEmpty(a)));
            var removeNames = new HashSet<string>(removals.Select(a => a.InternalName).Where(a => !string.IsNullOrEmpty(a)));
            foreach (var removal in removals)
            {
                var loaded = removal.Load();
                if (!string.IsNullOrEmpty(loaded?.name))
                    removeNames.Add(loaded.name);
            }

            foreach (var ee in kee.Load(character.m_RuntimeGender, character.m_RuntimeRace) ?? Enumerable.Empty<EquipmentEntity>())
            {
                if (ee == null) continue;

                if (removeNames.Any(a => EeInfraStructure.IsSameEquipmentFamily(ee.name, a)))
                    return true;

                var info = ee.ToEEInfo();
                if (info != null && removeGuids.Contains(info.Value.GUID))
                    return true;
            }

            return false;
        }

        private static void RestoreFilteredSettings(FilterState state)
        {
            if (state?.Settings == null) return;

            if (state.Settings.CommonSettings != null)
            {
                state.Settings.CommonSettings.m_EquipmentEntities = state.Common;
                state.Settings.CommonSettings.FXs = state.CommonFXs;
            }
            if (state.Settings.InGameSettings != null)
            {
                state.Settings.InGameSettings.m_EquipmentEntities = state.InGame;
                state.Settings.InGameSettings.FXs = state.InGameFXs;
            }
            if (state.Settings.DollRoomSettings != null)
            {
                state.Settings.DollRoomSettings.m_EquipmentEntities = state.DollRoom;
                state.Settings.DollRoomSettings.FXs = state.DollRoomFXs;
            }

            state.Settings.ColorRamps = state.ColorRamps;
        }

        public static void Postfix(Character __instance, FilterState __state)
        {
            var wasApplyingVa2EeSettings = s_ApplyingVa2EeSettings;
            try
            {
                var unit = FindUnitForCharacter(__instance);
                var clearedFilteredRamps = 0;
                var clearedFilteredDollRamps = 0;
                if (__state?.Removed > 0 && __state.ColorRamps?.Length > 0)
                {
                    clearedFilteredRamps = ClearAdditionalVisualRampIndices(
                        __instance,
                        __state.ColorRamps,
                        nameof(Character.ApplyAdditionalVisualSettings),
                        unit?.CharacterName);
                    clearedFilteredDollRamps = ClearAdditionalVisualDollDataRampIndices(
                        unit,
                        __state.ColorRamps,
                        nameof(Character.ApplyAdditionalVisualSettings),
                        unit?.CharacterName);
                }

                RestoreFilteredSettings(__state);
                if (!wasApplyingVa2EeSettings)
                {
                    ApplySavedSettings(__instance, unit, nameof(Character.ApplyAdditionalVisualSettings));
                    if (clearedFilteredRamps > 0 || clearedFilteredDollRamps > 0)
                        ForceVisualRebuildAfterRemovals(__instance, nameof(Character.ApplyAdditionalVisualSettings), unit?.CharacterName);
                }
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
            finally
            {
                RestoreFilteredSettings(__state);
                if (!wasApplyingVa2EeSettings)
                    s_ApplyingVa2EeSettings = false;
            }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(DollRoom), nameof(DollRoom.SetupInfo))]
    public static class DollRoom_SetupInfo_EE_Patch
    {
        public static void Prefix(
            UnitEntityData player,
            ref Kingmaker.Blueprints.Classes.BlueprintClassAdditionalVisualSettings additionalVisualSettings)
        {
            additionalVisualSettings = Character_ApplyAdditionalVisualSettings_Patch.PrepareFilteredAdditionalVisualSettings(
                player,
                additionalVisualSettings,
                nameof(DollRoom.SetupInfo));
        }

        public static void Postfix(DollRoom __instance, UnitEntityData player)
        {
            try
            {
                Character_ApplyAdditionalVisualSettings_Patch.ApplySavedSettings(
                    __instance?.m_Avatar,
                    player ?? __instance?.Unit,
                    nameof(DollRoom.SetupInfo));
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
            finally
            {
                Character_ApplyAdditionalVisualSettings_Patch.ClearGuard();
            }
        }
    }

    public abstract class EEApplyAction
    {
        public EEApplyAction(string guid, string internalName = null)
        {
            GUID = guid;
            InternalName = internalName;
        }

        public string GUID;
        public string InternalName;
        public abstract void Apply(UnitEntityData unitData, CharacterSettings settings);
    }

    public class AddEE : EEApplyAction
    {
        public EE_Applier.ColorInfo PrimaryCol;
        public EE_Applier.ColorInfo SecondaryCol;

        public AddEE(string guid, string internalName = null) : base(guid, internalName)
        {
        }

        public override void Apply(UnitEntityData unitData, CharacterSettings settings)
        {
            var character = unitData.View?.CharacterAvatar;
            var loadedEE = ResourcesLibrary.TryGetResource<EquipmentEntity>(GUID);
            if (character == null || loadedEE == null) return;

            if (!character.EquipmentEntities.Any(a => a.name == loadedEE.name))
                character.AddEquipmentEntity(loadedEE);

            settings.EeSettings.EEs.RemoveAll(a =>
                a.GUID == this.GUID && a.actionType == EE_Applier.ActionType.Remove);

            var applier = new EE_Applier(this.GUID, EE_Applier.ActionType.Add) { InternalName = this.InternalName ?? loadedEE.name };
            applier.Primary = this.PrimaryCol;
            applier.Secondary = this.SecondaryCol;
            settings.EeSettings.EEs.RemoveAll(a =>
                a.GUID == this.GUID && a.actionType == EE_Applier.ActionType.Add);
            settings.EeSettings.EEs.Add(applier);

            this.PrimaryCol?.Apply(loadedEE, character);
            this.SecondaryCol?.Apply(loadedEE, character);
        }
    }

    public class RemoveEE : EEApplyAction
    {
        public RemoveEE(string guid, string internalName = null) : base(guid, internalName)
        {
        }

        public override void Apply(UnitEntityData unitData, CharacterSettings settings)
        {
            var character = unitData.View?.CharacterAvatar;
            if (character == null) return;

            EeInfraStructure.RemoveEquipmentEntity(character, this.GUID, this.InternalName, true);

            settings.EeSettings.EEs.RemoveAll(a => a.GUID == this.GUID);
            settings.EeSettings.EEs.Add(new EE_Applier(this.GUID, EE_Applier.ActionType.Remove) { InternalName = this.InternalName });
        }
    }
}
