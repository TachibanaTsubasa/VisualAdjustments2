using Kingmaker;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Class.LevelUp;
using Kingmaker.UnitLogic.Parts;
using Owlcat.Runtime.UI.MVVM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kingmaker.EntitySystem.Entities;
using UniRx;
using Owlcat.Runtime.UniRx;
using VisualAdjustments2.Infrastructure;

namespace VisualAdjustments2.UI
{
    public class DollVM : BaseDisposable, IDisposable, IViewModel, IBaseDisposable
    {
        private static DollVM s_Active;

        public DollVM()
        {
            s_Active = this;
            base.AddDisposable(Game.Instance.SelectionCharacter.SelectedUnit.Subscribe((UnitReference _) =>
            {
                OnCharacterChanged();
            }));
            OnCharacterChanged(true);
        }

        public static void RefreshCurrentDoll(UnitEntityData unit, string source)
        {
            try
            {
                var active = s_Active;
                if (active == null || active.IsDisposed || unit == null)
                {
                    Main.DebugLog($"Skipped VA2 doll VM refresh after {source}: active={active != null}, disposed={active?.IsDisposed}, unit={unit?.CharacterName}");
                    return;
                }
                if (Game.Instance?.SelectionCharacter?.SelectedUnit?.Value.Value != unit)
                {
                    Main.DebugLog($"Skipped VA2 doll VM refresh after {source}: selected={Game.Instance?.SelectionCharacter?.SelectedUnit?.Value.Value?.CharacterName}, target={unit.CharacterName}");
                    return;
                }

                active.OnCharacterChanged(true);
                Main.DebugLog($"Refreshed VA2 doll VM after {source}: {unit.CharacterName}");
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
        }

        public static bool HasActiveFor(UnitEntityData unit)
        {
            try
            {
                return s_Active != null &&
                       !s_Active.IsDisposed &&
                       unit != null &&
                       Game.Instance?.SelectionCharacter?.SelectedUnit?.Value.Value == unit;
            }
            catch
            {
                return false;
            }
        }

        public void AddUnitPart()
        {
            try
            {
                var dolldata = Game.Instance.SelectionCharacter.SelectedUnit.Value.Value.Parts.Add<UnitPartDollData>();
                var data = dolldata.SetupForStoryCompanion();
                //dolldata.Default = data;
                dolldata.SetDefault(data);
                Game.Instance.SelectionCharacter.SelectedUnit.Value.Value.Descriptor.ForceUseClassEquipment =
                    true; //Game.Instance.SelectionCharacter.SelectedUnit.Value.Unit.GetSettings().ClassOverride.HasCustomOutfit; //They naked if we use HasCustomOutfit
                Game.Instance.SelectionCharacter.SelectedUnit.Value.Value.RebuildCharacter();
                OnCharacterChanged(true);
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
        }

        public void RemoveUnitPart()
        {
            try
            {
                Game.Instance.SelectionCharacter.SelectedUnit.Value.Value.Parts.Remove<UnitPartDollData>();
                Game.Instance.SelectionCharacter.SelectedUnit.Value.Value.Descriptor.ForceUseClassEquipment = Game
                    .Instance.SelectionCharacter.SelectedUnit.Value.Value.GetSettings().ClassOverride.HasCustomOutfit;
                OnCharacterChanged(true);
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
        }

        public void OnCharacterChanged(bool forcechange = false)
        {
            try
            {
                if (this.IsDisposed) return;
                var unit = Game.Instance.SelectionCharacter.SelectedUnit.Value.Value;
#if DEBUG
                Main.Logger.Log($"DollVM.OnCharacterChanged, Character changed to {unit.CharacterName}");
#endif
                if (unit.Get<UnitPartDollData>() != null &&
                    (forcechange || this.createDollVM?.Value?.charname != unit.CharacterName))
                {
                    var doll = unit.GetDollState();
                    Main.DebugLog($"VA2 DollVM binding doll: unit={unit.CharacterName}, force={forcechange}, doll={Character_ApplyAdditionalVisualSettings_Patch.DescribeSerializedDoll(unit.GetSettings()?.doll)}");
                    if (doll.Race != null)
                    {
                        doll.CreateTattos(default);
                        doll.CreateWarpaints(default, doll.Race.RaceId);
                        // Main.Logger.Log("NotNullRace");
                        //var lvlcontroller = new LevelUpController(unit, false, LevelUpState.CharBuildMode.SetName);
                        //Main.Logger.Log("AfterLvlCtor");
                        //lvlcontroller.Doll = doll;
                        CharGenAppearancePhaseVMModified.pcview.gameObject.SetActive(true);
                        CharGenAppearancePhaseVMModified.pcview.transform.parent.gameObject.SetActive(true);
                        this.m_DollAppearanceVM.Value?.Dispose();
                        var appearanceVM = new CharGenAppearancePhaseVMModified( /*lvlcontroller,*/ doll, false);
                        base.AddDisposable(appearanceVM);
                        this.m_DollAppearanceVM.Value = appearanceVM;
                        ///Fails after here somewhere
                        //Main.Logger.Log("AfterBind");
                    }
                    else
                    {
                        //Main.Logger.Log("NullRace");
                        doll.SetRace(Game.Instance.BlueprintRoot.Progression.HumanRace);
                        doll.CreateTattos(default);
                        doll.CreateWarpaints(default, doll.Race.RaceId);
                        var newvm = new CreateDollVM();
                        base.AddDisposable(this.createDollVM.Value = newvm);
                        this.m_DollAppearanceVM.Value?.Dispose();
                        // this.m_DollAppearanceVM = null;
                        //  newvm.AddDisposable(Game.Instance.SelectionCharacter.SelectedUnit.Subscribe((UnitDescriptor dat) => { if (dat.CharacterName != newvm.charname) { newvm.Dispose(); this.m_DollAppearanceVM.Value?.Dispose(); this.ShowWindow(VisualWindowType.Doll); } }));
                    }

                    this.createDollVM.Value?.Dispose();
                    // this.createDollVM.Value = null;
                    return;
                }
                else if (this.createDollVM.Value == null || forcechange ||
                         this.createDollVM?.Value?.charname != unit.CharacterName)
                {
                    //Main.Logger.Log("notDollData");
                    var newvm = new CreateDollVM();
                    base.AddDisposable(this.createDollVM.Value = newvm);
                    this.m_DollAppearanceVM?.Value?.Dispose();
                    // this.m_DollAppearanceVM = null;
                    // newvm.AddDisposable(Game.Instance.SelectionCharacter.SelectedUnit.Subscribe((UnitDescriptor _) => { newvm.Dispose(); this.m_DollAppearanceVM.Value?.Dispose(); this.ShowWindow(VisualWindowType.Doll); }));
                    // newvm.AddDisposable(Game.Instance.SelectionCharacter.SelectedUnit.Subscribe((UnitDescriptor _) => { newvm.Dispose(); this.ShowWindow(VisualWindowType.Doll); }));
                }
            }
            catch (Exception e)
            {
                Main.Logger.Error(e.ToString());
            }
        }

        public override void DisposeImplementation()
        {
            if (s_Active == this)
                s_Active = null;
            // Main.Logger.Log("DisposedDOllVM");
        }

        public ReactiveProperty<CreateDollVM> createDollVM = new ReactiveProperty<CreateDollVM>();

        public ReactiveProperty<CharGenAppearancePhaseVMModified> m_DollAppearanceVM =
            new ReactiveProperty<CharGenAppearancePhaseVMModified>();
    }
}
