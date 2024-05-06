/* Umbra.Game | (c) 2024 by Una         ____ ___        ___.
 * Licensed under the terms of AGPL-3  |    |   \ _____ \_ |__ _______ _____
 *                                     |    |   //     \ | __ \\_  __ \\__  \
 * https://github.com/una-xiv/umbra    |    |  /|  Y Y  \| \_\ \|  | \/ / __ \_
 *                                     |______//__|_|  /____  /|__|   (____  /
 *     Umbra.Game is free software: you can          \/     \/             \/
 *     redistribute it and/or modify it under the terms of the GNU Affero
 *     General Public License as published by the Free Software Foundation,
 *     either version 3 of the License, or (at your option) any later version.
 *
 *     Umbra.Game is distributed in the hope that it will be useful,
 *     but WITHOUT ANY WARRANTY; without even the implied warranty of
 *     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *     GNU Affero General Public License for more details.
 */

using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.GeneratedSheets;
using Umbra.Common;

namespace Umbra.Game;

[Service]
internal sealed class CompanionManager : ICompanionManager
{
    private const uint GysahlGreensIconId   = 4868;
    private const uint FollowActionIconId   = 902;
    private const uint FreeStanceIconId     = 906;
    private const uint DefenderStanceIconId = 903;
    private const uint AttackerStanceIconId = 904;
    private const uint HealerStanceIconId   = 905;

    public bool   HasGysahlGreens { get; private set; }
    public string CompanionName   { get; private set; } = string.Empty;
    public float  TimeLeft        { get; private set; }
    public ushort Level           { get; private set; }
    public uint   CurrentXp       { get; private set; }
    public uint   RequiredXp      { get; private set; }
    public uint   IconId          { get; private set; } = 25218;
    public string ActiveCommand   { get; private set; } = string.Empty;
    public bool   IsActive        { get; private set; }

    private readonly IDataManager _dataManager;
    private readonly IObjectTable _objectTable;
    private readonly Player       _player;

    public CompanionManager(IDataManager dataManager, IObjectTable objectTable, Player player)
    {
        _dataManager = dataManager;
        _objectTable = objectTable;
        _player      = player;

        OnTick();
    }

    [OnTick(interval: 1000)]
    public unsafe void OnTick()
    {
        UIState* ui = UIState.Instance();
        if (ui == null) return;

        var buddy = ui->Buddy.CompanionInfo;
        if (buddy.Rank == 0) return;

        var rank = _dataManager.GetExcelSheet<BuddyRank>()!.GetRow(buddy.Rank);
        if (rank == null) return;

        uint objectId = ui->Buddy.Companion.ObjectID;

        IsActive        = objectId > 0 && null != _objectTable.SearchById(objectId);
        HasGysahlGreens = _player.HasItemInInventory(GysahlGreensIconId);
        CompanionName   = buddy.Name;
        TimeLeft        = buddy.TimeLeft;
        Level           = buddy.Rank;
        CurrentXp       = buddy.CurrentXP;
        RequiredXp      = rank.ExpRequired;
        IconId          = GetIconId(buddy);

        ActiveCommand = buddy.ActiveCommand switch {
            3 => I18N.Translate("CompanionWidget.Follow"),
            4 => I18N.Translate("CompanionWidget.Free"),
            5 => I18N.Translate("CompanionWidget.Tank"),
            6 => I18N.Translate("CompanionWidget.DPS"),
            7 => I18N.Translate("CompanionWidget.Heal"),
            _ => "???",
        };
    }

    public bool CanSummon()
    {
        return HasGysahlGreens
         && _player is { IsBoundByDuty: false, IsOccupied: false, IsCasting: false, IsDead: false };
    }

    public void Summon()
    {
        if (!HasGysahlGreens) {
            Logger.Warning("You do not have any Gysahl Greens to summon your companion.");
            return;
        }

        if (!CanSummon()) {
            Logger.Warning("You cannot summon your companion at this time.");
            return;
        }

        _player.UseInventoryItem(GysahlGreensIconId);
    }

    public unsafe void Dismiss()
    {
        if (TimeLeft < 1) return;

        ActionManager* am = ActionManager.Instance();
        if (am == null) return;

        am->UseAction(ActionType.BuddyAction, 2);
    }

    public unsafe void SwitchBehavior()
    {
        if (TimeLeft < 1) return;

        UIState* ui = UIState.Instance();
        if (ui == null) return;

        var buddy = ui->Buddy.CompanionInfo;
        if (buddy.Rank == 0) return;

        ActionManager* am = ActionManager.Instance();
        if (am == null) return;

        am->UseAction(ActionType.BuddyAction, FindNextValidActiveCommand(buddy.ActiveCommand));
    }

    private static unsafe uint FindNextValidActiveCommand(uint currentCommand)
    {
        UIState* ui = UIState.Instance();
        if (ui == null) return 4; // Free

        var buddy = ui->Buddy.CompanionInfo;
        if (buddy.Rank == 0) return 4; // Free

        return currentCommand switch {
            3 => 4,                                                           // Follow -> Free Stance
            4 => buddy.DefenderLevel > 0 ? 5 : FindNextValidActiveCommand(5), // Free Stance -> Defender Stance
            5 => buddy.AttackerLevel > 0 ? 6 : FindNextValidActiveCommand(6), // Defender Stance -> Attacker Stance
            6 => buddy.HealerLevel   > 0 ? 7 : FindNextValidActiveCommand(7), // Attacker Stance -> Healer Stance
            7 => 3,                                                           // Healer Stance -> Follow
            _ => 3,                                                           // Unknown -> Follow
        };
    }

    private static uint GetIconId(CompanionInfo info)
    {
        if (info.TimeLeft < 1) {
            return 25218; // Gysahl Greens.
        }

        return info.ActiveCommand switch {
            3 => FollowActionIconId,   // Follow
            4 => FreeStanceIconId,     // Free Stance
            5 => DefenderStanceIconId, // Defender Stance
            6 => AttackerStanceIconId, // Attacker Stance
            7 => HealerStanceIconId,   // Healer Stance
            _ => 25218,
        };
    }
}
