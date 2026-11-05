using System.Net;

using Lunarite.Network.Ue3;

internal sealed partial class TestSuite
{
    private void GatewayDecoderAndItemCatalog()
    {
        Check(ReadGatewayField("ADFF2F400000C09F0000803F04", 99) == 45,
            "gateway net-cache decoder identifies Kismet_ClientPlaySound");
        Check(ReadGatewayField("AEFF2F80D6FF17200000E04F0000C01F02", 130) == 46,
            "gateway net-cache decoder identifies Kismet_ClientStopSound");
        var gatewayPackageMap = new Ue3PackageMapCatalog();
        bool loadedGatewayPackage = gatewayPackageMap.AddUse(new(
            "HuxleyGame", "HuxleyGame.u", "test-huxley-game", 0), out string gatewayPackageError);
        var firstGatewayObject = gatewayPackageMap.Resolve(0);
        // The experimental gateway reads the client's HuxleyGame.u through UELib (Eliot.UELib.dll),
        // which is not part of this repository; without it, or without a client install, the check
        // is skipped.
        if (!loadedGatewayPackage && gatewayPackageError.Contains("Eliot.UELib.dll not found"))
            Console.WriteLine("SKIP: gateway package map (Eliot.UELib.dll not installed)");
        else if (!loadedGatewayPackage && !Directory.Exists(Lunarite.Resources.ClientFolder.Root))
            Console.WriteLine($"SKIP: gateway package map (no client at {Lunarite.Resources.ClientFolder.Root})");
        else Check(loadedGatewayPackage && gatewayPackageMap.MaxObjectIndex > 0
            && firstGatewayObject is { Package: "HuxleyGame" } && firstGatewayObject.Path.Length > 0
            && gatewayPackageMap.MaxNameIndex > 0 && gatewayPackageMap.ResolveName(0)?.Length > 0,
            $"gateway package map resolves ordered static UE3 exports ({gatewayPackageError})");
        _questGateway.ConfigureRoom(820100, new byte[16], hostAddress: IPAddress.Parse("192.0.2.44"), ownerPid: 920099);
        _questGateway.ConfigureRoom(870100, Enumerable.Repeat((byte)0xA5, 16).ToArray(),
            hostAddress: IPAddress.Parse("198.51.100.77"), ownerPid: 920098, hostPort: 7778);
        Check(_questGateway.TargetEndpoint(920099)?.Equals(new IPEndPoint(IPAddress.Parse("192.0.2.44"), 7777)) == true
            && _questGateway.TargetEndpoint(920098)?.Equals(new IPEndPoint(IPAddress.Parse("198.51.100.77"), 7778)) == true,
            "parallel quest gateways retain independent remote listen-host endpoints");
        Check(_questGateway.CancelHandshake(920098)
            && _questGateway.TargetEndpoint(920099) != null
            && _questGateway.TargetEndpoint(920098) == null
            && _questGateway.CancelHandshake(920099),
            "canceling one quest gateway does not tear down another host's transport");
        Check(_itemCatalog.Get(604221) is { Price: 128, SellPrice: 128, MaxCollapseCount: 99 },
            "exported item catalog resolves retail price/sell/stack data for item 604221");
        Check(_itemCatalog.Get(100499) is { Price: 500 },
            "exported item catalog resolves full wire ids from the weapon table");
        Check(_itemCatalog.Get(100499) is { Repairable: true, EquipLevel: 1, ItemGrade: 0 },
            "exported item catalog preserves retail repair class, level, and default grade");
        Check(_itemCatalog.Get(210001) is { ClassName: "HuxleyMMOItemData_Helmet", Repairable: true }
            && _itemCatalog.Get(210101) is { ClassName: "HuxleyMMOItemData_Upbody", Repairable: true }
            && _playerStore.CreateAcquiredItem(210001).Durability == 100
            && _playerStore.CreateAcquiredItem(210101).Durability == 100,
            "armor subclasses inherit repairable durability from exported HuxleyArmorData");
        const int inheritedArmorRepairPid = 920056;
        ResetCharacter(inheritedArmorRepairPid);
        _playerStore.GetProgress(inheritedArmorRepairPid).QT = 10_000;
        var inheritedHelmet = _playerStore.CreateAcquiredItem(210001);
        inheritedHelmet.InventoryIndex = 4; inheritedHelmet.InventoryCell = 0; inheritedHelmet.Durability = 50;
        _playerStore.GetInventory(inheritedArmorRepairPid, () => []).Add(inheritedHelmet);
        Check(_playerStore.TryRepair(inheritedArmorRepairPid, 4, 0, false,
                _licenseStore.RepairDiscountPercent(inheritedArmorRepairPid),
                out var inheritedArmorRepaired, out long inheritedArmorQt)
            && inheritedArmorRepaired is [{ ItemId: 210001, Durability: 100 }]
            && inheritedArmorQt < 10_000,
            "inherited helmet armor can use the authoritative QT repair transaction");
        Check(_itemCatalog.Get(138401) is { OnlyQtItem: true, QtPrice: 1000, AdditionalQt: 0 }
            && _itemCatalog.Get(140601) is { OnlyForSuperUser: true }
            && _itemCatalog.Get(120396) is { Enabled: false }
            && _itemCatalog.Get(604221) is { Enabled: true }
            && _itemCatalog.Get(120681) is
                { ItemLimitTimeType: 3, FixedLimitTime: 1236243600 },
            "item catalog preserves QT-only, GM-only, and fixed-expiration authority");
        Check(_itemCatalog.DisabledCount == 145,
            "item catalog preserves every unique cooked bEnabled=false item definition");
        _payItems = _itemCatalog.PayItems;
        var payItemClasses = _payItems.GroupBy(x => x.ClassName).ToDictionary(x => x.Key, x => x.Count());
        Check(_payItems.Count == 55
            && _payItems.Select(x => x.EntryIndex).Distinct().Count() == 55
            && payItemClasses.GetValueOrDefault("HuxleyCityItemData_HPointCoupon") == 19
            && payItemClasses.GetValueOrDefault("HuxleyCityItemData_IncreaseContamination") == 8
            && payItemClasses.GetValueOrDefault("HuxleyCityItemData_OpenWeaponSlot") == 8
            && payItemClasses.GetValueOrDefault("HuxleyCityItemData_TermLicense") == 7
            && payItemClasses.GetValueOrDefault("HuxleyCityItemData_PersonalRespawn") == 4
            && payItemClasses.GetValueOrDefault("HuxleyCityItemData_CityWarpPass") == 3
            && payItemClasses.GetValueOrDefault("HuxleyCityItemData_ResetTuneup") == 2
            && payItemClasses.GetValueOrDefault("HuxleyCityItemData_EnlargeBank") == 1
            && payItemClasses.GetValueOrDefault("HuxleyCityItemData_EnlargeInventory") == 1
            && payItemClasses.GetValueOrDefault("HuxleyCityItemData_RemoveItemBind") == 1
            && payItemClasses.GetValueOrDefault("HuxleyCityItemData_ResetBattleRecord") == 1,
            "all 55 cooked HuxleyItemTable.PayItem definitions retain authoritative cash-item membership");
    }
}
