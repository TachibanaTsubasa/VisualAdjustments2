using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.BundlesLoading;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UI.ServiceWindow;
using Kingmaker.UnitLogic;
using Kingmaker.Visual.CharacterSystem;
using Owlcat.Runtime.UI.MVVM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniRx;
using UnityEngine;
using VisualAdjustments2.Infrastructure;
using Owlcat.Runtime.UniRx;

namespace VisualAdjustments2.UI
{
    [HarmonyLib.HarmonyPatch(typeof(Character), nameof(Character.CopyEquipmentFrom))]
    public static class Character_CopyEquipmentFrom_Patch
    {
        //Prevents equipment from being reset in the preview
        public static bool Prefix(Character __instance)
        {
            if (ServiceWindowsVM_ShowWindow_Patch.swPCView?.m_EEPickerPCView?.ViewModel != null && __instance.EquipmentEntities?.Count != 0)
            {
                return false;
            }
            else return true;
        }
    }
    public class EEPickerVM : BaseDisposable, IDisposable, IViewModel, IBaseDisposable
    {
        public Dictionary<string, EEApplyAction> applyActions = new Dictionary<string, EEApplyAction>();

        private static IEnumerable<EquipmentEntity> GetCurrentEquipmentEntities(UnitEntityData unit)
        {
            var character = unit?.View?.CharacterAvatar;
            if (character == null) yield break;

            foreach (var ee in character.EquipmentEntities ?? Enumerable.Empty<EquipmentEntity>())
                yield return ee;

            foreach (var ee in character.AdditionalEquipmentEntities ?? Enumerable.Empty<EquipmentEntity>())
                yield return ee;
        }

        private static void ReapplySavedEeSettings(UnitEntityData unit)
        {
            var settings = unit?.GetSettings();
            if (settings == null) return;

            var removals = settings.EeSettings.EEs?
                .Where(a => a.actionType == EE_Applier.ActionType.Remove)
                .ToList() ?? new List<EE_Applier>();

            var characterAvatar = unit.View?.CharacterAvatar;
            if (characterAvatar != null)
            {
                var changed = EeInfraStructure.ApplySettings(settings, characterAvatar);
                var clearedRamps = Character_ApplyAdditionalVisualSettings_Patch.ClearAdditionalVisualRampIndices(
                    characterAvatar,
                    removals,
                    "EEPicker reset",
                    unit.CharacterName);
                var clearedDollRamps = Character_ApplyAdditionalVisualSettings_Patch.ClearAdditionalVisualDollDataRampIndices(
                    characterAvatar,
                    unit,
                    removals,
                    "EEPicker reset",
                    unit.CharacterName);
                var clearedSerializedDollRamps = Character_ApplyAdditionalVisualSettings_Patch.ClearAdditionalVisualSerializedDollRampIndices(
                    characterAvatar,
                    settings,
                    removals,
                    "EEPicker reset",
                    unit.CharacterName);
                if (changed > 0 || clearedRamps > 0 || clearedDollRamps > 0 || clearedSerializedDollRamps > 0)
                    Character_ApplyAdditionalVisualSettings_Patch.ForceVisualRebuildAfterRemovals(characterAvatar, "EEPicker reset", unit.CharacterName);
            }

            var dollRoomAvatar = Game.Instance?.UI?.Common?.DollRoom?.m_Avatar;
            if (dollRoomAvatar != null)
            {
                var changed = EeInfraStructure.ApplySettings(settings, dollRoomAvatar);
                var clearedRamps = Character_ApplyAdditionalVisualSettings_Patch.ClearAdditionalVisualRampIndices(
                    dollRoomAvatar,
                    removals,
                    "EEPicker reset doll",
                    unit.CharacterName);
                var clearedDollRamps = Character_ApplyAdditionalVisualSettings_Patch.ClearAdditionalVisualDollDataRampIndices(
                    dollRoomAvatar,
                    unit,
                    removals,
                    "EEPicker reset doll",
                    unit.CharacterName);
                var clearedSerializedDollRamps = Character_ApplyAdditionalVisualSettings_Patch.ClearAdditionalVisualSerializedDollRampIndices(
                    dollRoomAvatar,
                    settings,
                    removals,
                    "EEPicker reset doll",
                    unit.CharacterName);
                if (changed > 0 || clearedRamps > 0 || clearedDollRamps > 0 || clearedSerializedDollRamps > 0)
                    Character_ApplyAdditionalVisualSettings_Patch.ForceVisualRebuildAfterRemovals(dollRoomAvatar, "EEPicker reset doll", unit.CharacterName);
            }

            Main.DebugLog($"EEPicker reset reapplied saved EE action count: {settings.EeSettings.EEs.Count}");
        }

        public void ResetChanges()
        {
            this.applyActions.Clear();
            ReapplySavedEeSettings(Game.Instance.SelectionCharacter.SelectedUnit.Value.Value);
            var CurrentReactive = new ReactiveCollection<ListViewItemVM>();
            foreach (var ee in GetCurrentEquipmentEntities(Game.Instance.SelectionCharacter.SelectedUnit.Value.Value))
            {
                //Main.Logger.Log(ee.name);
                var inf = ee.ToEEInfo();
                if (inf != null && !CurrentReactive.Any(a => a.Guid == inf.Value.GUID))
                {
                    CurrentReactive.Add(new ListViewItemVM(inf.Value, false, RemoveListItem,true));
                }
            }
            CurrentEEs.Value?.Dispose();
            base.AddDisposable(CurrentEEs.Value = new ListViewVM(CurrentReactive, new ReactiveProperty<ListViewItemVM>(CurrentReactive.FirstOrDefault())));
        }

        public IReactiveProperty<UnitReference> UnitDescriptor;
        public ReactiveProperty<ListViewVM> AllEEs = new ReactiveProperty<ListViewVM>();
        public ReactiveProperty<ListViewVM> CurrentEEs = new ReactiveProperty<ListViewVM>();
        public EEPickerVM(UnitEntityData data)
        {
            ReactiveCollection<ListViewItemVM> reactive = new ReactiveCollection<ListViewItemVM>();
            foreach (var kv in ResourceLoader.AllEEs)
            {
                reactive.Add(new ListViewItemVM(kv, true, AddListItem,true));
            }
            var CurrentReactive = new ReactiveCollection<ListViewItemVM>();
            foreach (var ee in GetCurrentEquipmentEntities(Game.Instance.SelectionCharacter.SelectedUnit.Value.Value))
            {
                //Main.Logger.Log(ee.name);
                var inf = ee.ToEEInfo();
                if (inf != null && !CurrentReactive.Any(a => a.Guid == inf.Value.GUID))
                {
                    CurrentReactive.Add(new ListViewItemVM(inf.Value, false, RemoveListItem,true));
                }
            }
            this.UnitDescriptor = Game.Instance.SelectionCharacter.SelectedUnit;
            base.AddDisposable(Game.Instance.SelectionCharacter.SelectedUnit.Subscribe(delegate (UnitReference _)
            {
                this.OnUnitChanged();
            }));
            base.AddDisposable(AllEEs.Value = new ListViewVM(reactive, new ReactiveProperty<ListViewItemVM>(null)));
            base.AddDisposable(CurrentEEs.Value = new ListViewVM(CurrentReactive, new ReactiveProperty<ListViewItemVM>(CurrentReactive.FirstOrDefault())));
            

            //CurrentEEs = new ListViewVM();
        }
        private void OnUnitChanged()
        {
            this.applyActions.Clear();
            var CurrentReactive = new ReactiveCollection<ListViewItemVM>();
            foreach (var ee in GetCurrentEquipmentEntities(Game.Instance.SelectionCharacter.SelectedUnit.Value.Value))
            {
                //Main.Logger.Log(ee.name);
                var inf = ee.ToEEInfo();
                if (inf != null && !CurrentReactive.Any(a => a.Guid == inf.Value.GUID))
                {
                    CurrentReactive.Add(new ListViewItemVM(inf.Value, false, RemoveListItem,true));
                }
            }
            CurrentEEs.Value?.Dispose();
            base.AddDisposable(CurrentEEs.Value = new ListViewVM(CurrentReactive, new ReactiveProperty<ListViewItemVM>(CurrentReactive.FirstOrDefault())));
        }
        public void RemoveListItem(ListViewItemVM item)
        {
            try
            {
                if (this.CurrentEEs?.Value?.EntitiesCollection?.Contains(item) == true)
                {

                    this.CurrentEEs?.Value?.EntitiesCollection?.Remove(item);
                    var dollRoomAvatar = Game.Instance.UI.Common.DollRoom.m_Avatar;
                    var changed = EeInfraStructure.RemoveEquipmentEntity(dollRoomAvatar, item.Guid, item.InternalName, true);
                    var pendingRemoval = new EE_Applier(item.Guid, EE_Applier.ActionType.Remove)
                    {
                        InternalName = item.InternalName
                    };
                    var clearedRamps = Character_ApplyAdditionalVisualSettings_Patch.ClearAdditionalVisualRampIndices(
                        dollRoomAvatar,
                        new List<EE_Applier> { pendingRemoval },
                        "EEPicker preview remove",
                        Game.Instance.UI.Common.DollRoom.Unit?.CharacterName);
                    var clearedDollRamps = Character_ApplyAdditionalVisualSettings_Patch.ClearAdditionalVisualDollDataRampIndices(
                        dollRoomAvatar,
                        Game.Instance.UI.Common.DollRoom.Unit,
                        new List<EE_Applier> { pendingRemoval },
                        "EEPicker preview remove",
                        Game.Instance.UI.Common.DollRoom.Unit?.CharacterName);
                    if (changed || clearedRamps > 0 || clearedDollRamps > 0)
                        Character_ApplyAdditionalVisualSettings_Patch.ForceVisualRebuildAfterRemovals(
                            dollRoomAvatar,
                            "EEPicker preview remove",
                            Game.Instance.UI.Common.DollRoom.Unit?.CharacterName);
                    this.applyActions[item.Guid] = new RemoveEE(item.Guid, item.InternalName);
                    Main.DebugLog($"EEPicker queued remove: {item.InternalName} ({item.Guid})");
                }
            }
            catch (Exception e)
            {

                Main.Logger.Error(e.ToString());
            }
        }
        public void AddListItem(ListViewItemVM item)
        {
            try
            {
                if (!this.CurrentEEs?.Value?.EntitiesCollection.Any(a => a.Guid == item.Guid) == true) this.CurrentEEs?.Value.EntitiesCollection.Add(new ListViewItemVM(item, false, RemoveListItem,true));
               // Main.Logger.Log(ResourcesLibrary.TryGetResource<EquipmentEntity>(item.Guid).ToString());
                if(!Game.Instance.UI.Common.DollRoom.m_Avatar.EquipmentEntities.Any(a => a.name == item.InternalName))
                Game.Instance.UI.Common.DollRoom.m_Avatar.AddEquipmentEntity(ResourcesLibrary.TryGetResource<EquipmentEntity>(item.Guid));
                this.applyActions[item.Guid] = new AddEE(item.Guid, item.InternalName);
                Main.DebugLog($"EEPicker queued add: {item.InternalName} ({item.Guid})");
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
        }
        public void ApplyColor(Color col, bool PrimOrSec)
        {
#if DEBUG
            Main.Logger.Log("TriedApply");
#endif
            try
            {
                if (this.applyActions.TryGetValue(this.CurrentEEs.Value.SelectedEntity.Value.Guid, out EEApplyAction val) && val.GetType() == typeof(AddEE))
                {
                    var loaded = ResourcesLibrary.TryGetResource<EquipmentEntity>(this.CurrentEEs.Value.SelectedEntity.Value.Guid);
                    var addee = (AddEE)val;
                    if (PrimOrSec && loaded.PrimaryColorsProfile?.Ramps?.Count > 0)
                    {
                        //Main.Logger.Log("Prim");
                        var ColInf = new EE_Applier.ColorInfo(PrimOrSec);
                        ColInf.CustomColor = true;
                        ColInf.CustomColorRGB = new SerializableColor(col);
                        addee.PrimaryCol = ColInf;
                        ColInf.Apply(loaded, Game.Instance.UI.Common.DollRoom.m_Avatar);
                        //Game.Instance.UI.Common.DollRoom.m_Avatar.IsDirty = true;
                    }
                    else if(loaded.SecondaryColorsProfile?.Ramps?.Count > 0)
                    {
                        //Main.Logger.Log("Sec");
                        var ColInf = new EE_Applier.ColorInfo(PrimOrSec);
                        ColInf.CustomColor = true;
                        ColInf.CustomColorRGB = new SerializableColor(col);
                        addee.SecondaryCol = ColInf;
                        ColInf.Apply(loaded, Game.Instance.UI.Common.DollRoom.m_Avatar);
                        //Game.Instance.UI.Common.DollRoom.m_Avatar.IsDirty = true;
                    }
                }
                else
                {
                    var ee = this.UnitDescriptor.Value.Value.GetSettings().EeSettings.EEs.FirstOrDefault(a => a.GUID == this.CurrentEEs.Value.SelectedEntity.Value.Guid);
                    var loaded = ResourcesLibrary.TryGetResource<EquipmentEntity>(this.CurrentEEs.Value.SelectedEntity.Value.Guid);
                    if (ee != null)
                    {
                        var addee = ee;
                        if (PrimOrSec && loaded.PrimaryColorsProfile?.Ramps?.Count > 0)
                        {
                            var ColInf = new EE_Applier.ColorInfo(PrimOrSec);
                            ColInf.CustomColor = true;
                            ColInf.CustomColorRGB = new SerializableColor(col);
                            addee.Primary = ColInf;
                            addee.Apply(Game.Instance.UI.Common.DollRoom.m_Avatar);
                            //Game.Instance.UI.Common.DollRoom.m_Avatar.IsDirty = true;

                            var addaction = new AddEE(addee.GUID);
                            addaction.PrimaryCol = ColInf;
                            addaction.SecondaryCol = addee.Secondary;
                            this.applyActions.Add(addaction.GUID, addaction);
                        }
                        else if(loaded.SecondaryColorsProfile?.Ramps?.Count > 0)
                        {
                            var ColInf = new EE_Applier.ColorInfo(PrimOrSec);
                            ColInf.CustomColor = true;
                            ColInf.CustomColorRGB = new SerializableColor(col);
                            addee.Secondary = ColInf;
                            addee.Apply(Game.Instance.UI.Common.DollRoom.m_Avatar);
                            //Game.Instance.UI.Common.DollRoom.m_Avatar.IsDirty = true;

                            var addaction = new AddEE(addee.GUID);
                            addaction.PrimaryCol = addee.Primary;
                            addaction.SecondaryCol = ColInf;
                            this.applyActions.Add(addaction.GUID, addaction);
                        }
                    }
                    else
                    {
                        var addee = new EE_Applier(this.CurrentEEs.Value.SelectedEntity.Value.Guid, EE_Applier.ActionType.Add);
                        if (PrimOrSec && loaded.PrimaryColorsProfile?.Ramps?.Count > 0)
                        {
                            var ColInf = new EE_Applier.ColorInfo(PrimOrSec);
                            ColInf.CustomColor = true;
                            ColInf.CustomColorRGB = new SerializableColor(col);
                            addee.Primary = ColInf;
                            addee.Apply(Game.Instance.UI.Common.DollRoom.m_Avatar);
                            //Game.Instance.UI.Common.DollRoom.m_Avatar.IsDirty = true;

                            var addaction = new AddEE(addee.GUID);
                            addaction.PrimaryCol = ColInf;
                            addaction.SecondaryCol = addee.Secondary;
                            this.applyActions.Add(addaction.GUID, addaction);
                        }
                        else if(loaded.SecondaryColorsProfile?.Ramps?.Count > 0)
                        {
                            var ColInf = new EE_Applier.ColorInfo(PrimOrSec);
                            ColInf.CustomColor = true;
                            ColInf.CustomColorRGB = new SerializableColor(col);
                            addee.Secondary = ColInf;
                            addee.Apply(Game.Instance.UI.Common.DollRoom.m_Avatar);
                            //Game.Instance.UI.Common.DollRoom.m_Avatar.IsDirty = true;

                            var addaction = new AddEE(addee.GUID);
                            addaction.PrimaryCol = addee.Primary;
                            addaction.SecondaryCol = ColInf;
                            this.applyActions.Add(addaction.GUID, addaction);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
        }
        public void ApplyColor(int rampIndex, bool PrimOrSec)
        {
#if DEBUG
            Main.Logger.Log("TriedApply");
#endif
            try
            {
                if (this.applyActions.TryGetValue(this.CurrentEEs.Value.SelectedEntity.Value.Guid, out EEApplyAction val) && val.GetType() == typeof(AddEE))
                {
                    var loaded = ResourcesLibrary.TryGetResource<EquipmentEntity>(this.CurrentEEs.Value.SelectedEntity.Value.Guid);
                    var addee = (AddEE)val;
                    if (PrimOrSec && loaded.PrimaryColorsProfile?.Ramps?.Count > 0)
                    {
                        //Main.Logger.Log("Prim");
                        var ColInf = new EE_Applier.ColorInfo(PrimOrSec);
                        ColInf.CustomColor = false;
                        ColInf.Index = rampIndex;
                        addee.PrimaryCol = ColInf;
                        ColInf.Apply(loaded, Game.Instance.UI.Common.DollRoom.m_Avatar);
                        //Game.Instance.UI.Common.DollRoom.m_Avatar.IsDirty = true;
                    }
                    else if (loaded.SecondaryColorsProfile?.Ramps?.Count > 0)
                    {
                        //Main.Logger.Log("Sec");
                        var ColInf = new EE_Applier.ColorInfo(PrimOrSec);
                        ColInf.CustomColor = false;
                        ColInf.Index = rampIndex;
                        addee.SecondaryCol = ColInf;
                        ColInf.Apply(loaded, Game.Instance.UI.Common.DollRoom.m_Avatar);
                        //Game.Instance.UI.Common.DollRoom.m_Avatar.IsDirty = true;
                    }
                  //  Kingmaker.Game.Instance.SelectionCharacter.SelectedUnit.Value.Unit.View.UpdateClassEquipment();
                  //  Kingmaker.Game.Instance.UI.Common.DollRoom.m_Avatar.UpdateCharacter();
                }
                else
                {
                    var ee = this.UnitDescriptor.Value.Value.GetSettings().EeSettings.EEs.FirstOrDefault(a => a.GUID == this.CurrentEEs.Value.SelectedEntity.Value.Guid);
                    var loaded = ResourcesLibrary.TryGetResource<EquipmentEntity>(this.CurrentEEs.Value.SelectedEntity.Value.Guid);
                    if (ee != null)
                    {
                        var addee = ee;
                        if (PrimOrSec && loaded.PrimaryColorsProfile?.Ramps?.Count > 0)
                        {
                            var ColInf = new EE_Applier.ColorInfo(PrimOrSec);
                            ColInf.CustomColor = false;
                            ColInf.Index = rampIndex;
                            addee.Primary = ColInf;
                            addee.Apply(Game.Instance.UI.Common.DollRoom.m_Avatar);
                            //Game.Instance.UI.Common.DollRoom.m_Avatar.IsDirty = true;

                            var addaction = new AddEE(addee.GUID);
                            addaction.PrimaryCol = ColInf;
                            addaction.SecondaryCol = addee.Secondary;
                            this.applyActions.Add(addaction.GUID, addaction);
                        }
                        else if (loaded.SecondaryColorsProfile?.Ramps?.Count > 0)
                        {
                            var ColInf = new EE_Applier.ColorInfo(PrimOrSec);
                            ColInf.CustomColor = false;
                            ColInf.Index = rampIndex;
                            addee.Secondary = ColInf;
                            addee.Apply(Game.Instance.UI.Common.DollRoom.m_Avatar);
                            //Game.Instance.UI.Common.DollRoom.m_Avatar.IsDirty = true;

                            var addaction = new AddEE(addee.GUID);
                            addaction.PrimaryCol = addee.Primary;
                            addaction.SecondaryCol = ColInf;
                            this.applyActions.Add(addaction.GUID, addaction);
                        }
                    }
                    else
                    {
                        var addee = new EE_Applier(this.CurrentEEs.Value.SelectedEntity.Value.Guid, EE_Applier.ActionType.Add);
                        if (PrimOrSec && loaded.PrimaryColorsProfile?.Ramps?.Count > 0)
                        {
                            var ColInf = new EE_Applier.ColorInfo(PrimOrSec);
                            ColInf.CustomColor = false;
                            ColInf.Index = rampIndex;
                            addee.Primary = ColInf;
                            addee.Apply(Game.Instance.UI.Common.DollRoom.m_Avatar);
                            //Game.Instance.UI.Common.DollRoom.m_Avatar.IsDirty = true;

                            var addaction = new AddEE(addee.GUID);
                            addaction.PrimaryCol = ColInf;
                            addaction.SecondaryCol = addee.Secondary;
                            this.applyActions.Add(addaction.GUID, addaction);
                        }
                        else if (loaded.SecondaryColorsProfile?.Ramps?.Count > 0)
                        {
                            var ColInf = new EE_Applier.ColorInfo(PrimOrSec);
                            ColInf.CustomColor = false;
                            ColInf.Index = rampIndex;
                            addee.Secondary = ColInf;
                            addee.Apply(Game.Instance.UI.Common.DollRoom.m_Avatar);
                            //Game.Instance.UI.Common.DollRoom.m_Avatar.IsDirty = true;

                            var addaction = new AddEE(addee.GUID);
                            addaction.PrimaryCol = addee.Primary;
                            addaction.SecondaryCol = ColInf;
                            this.applyActions.Add(addaction.GUID, addaction);
                        }
                    }
                }
                Kingmaker.Game.Instance.SelectionCharacter.SelectedUnit.Value.Value.View.UpdateClassEquipment();
                Kingmaker.Game.Instance.SelectionCharacter.SelectedUnit.Value.Value.View.CharacterAvatar.UpdateCharacter();


                Kingmaker.Game.Instance.UI.Common.DollRoom.Unit.View.UpdateClassEquipment();
                Kingmaker.Game.Instance.UI.Common.DollRoom.m_Avatar.UpdateCharacter();
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
        }
        public override void DisposeImplementation()
        {

        }
    }
}
