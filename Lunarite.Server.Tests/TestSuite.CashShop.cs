using System.Net;
using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

internal sealed partial class TestSuite
{
    private async Task CashShop1Async()
    {
        Check(_itemCatalog.Get(678201) is { IsPayItem: true }
            && _itemCatalog.Get(604221) is { IsPayItem: false },
            "cash-item membership follows the cooked table reference rather than an item-id heuristic");
        var cashExecution = _cashShopCatalog.AuditExecutionCoverage();
        Check(cashExecution.Products == 55
            && cashExecution.UnsupportedItems.Count == 0
            && cashExecution.Routes.GetValueOrDefault(CashShopCatalog.ExecutionRoute.UseItem) == 52
            && cashExecution.Routes.GetValueOrDefault(CashShopCatalog.ExecutionRoute.ResetTuneUp) == 2
            && cashExecution.Routes.GetValueOrDefault(CashShopCatalog.ExecutionRoute.ResetBind) == 1,
            "every cooked cash product has an authoritative inventory-use or target-selection transaction");
        var cashEntries = _cashShopCatalog.Entries;
        var cashCategories = CashShopCatalog.CategoryFile().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var cashProducts = _cashShopCatalog.ProductFile().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var cashPackages = _cashShopCatalog.PackageFile().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var cashProductFields = cashProducts.Select(x => x.Split('@')).ToArray();
        var cashPackageFields = cashPackages.Select(x => x.Split('@')).ToArray();
        Check(cashEntries.Count == 55 && cashEntries.Select(x => x.PackageId).Distinct().Count() == 55
            && cashCategories.Length == 11 && cashCategories.All(x => x.Split('@').Length == 7)
            && cashCategories[0].Split('@') is ["228", "Free Shop", "200", "201", "228", "1", "1"]
            && cashCategories.Skip(1).Where((_, index) => index % 2 == 0)
                .All(x => x.Split('@')[4] == "228")
            && cashProducts.Length == 55 && cashProductFields.All(x => x.Length == 17)
            && cashPackages.Length == 55 && cashPackageFields.All(x => x.Length == 25)
            && cashPackageFields.All(x => x[5] == "0")
            && cashEntries.All(x => _cashShopCatalog.FindPackage(x.PackageId)?.ItemId == x.ItemId),
            "local ProductTransfer catalog exposes every cooked pay item with native field counts and zero prices");
        Check(cashEntries.Zip(cashProductFields).All(x => x.Second[0] == x.First.ItemId.ToString()
                && x.Second[13] == x.First.ItemId.ToString())
            && cashEntries.Zip(cashPackageFields).All(x => x.Second[19] == x.First.ItemId.ToString()
                && x.Second[20] == x.First.IconName)
            && cashEntries.All(x => ClientAssetManifest.ContainsPath($"TextureIngameShop.{x.IconName}")),
            "product IDs preserve cooked-item lookup while every package icon resolves in the client asset manifest");
        Check(ClientAssetManifest.ContainsPath("TextureIngameShop.InGameShopItem_01")
            && ClientAssetManifest.ContainsPath("textureingameshop.ingameshopitem_153")
            && !ClientAssetManifest.ContainsPath("TextureIngameShop.670101"),
            "asset manifest resolves exact shipped exports case-insensitively and rejects numeric placeholder paths");
        Check(cashEntries.Single(x => x.ItemId == 678201).IconName == "InGameShopItem_60"
            && cashEntries.Single(x => x.ItemId == 676001).IconName == "InGameShopItem_54"
            && cashEntries.Single(x => x.ItemId == 676002).IconName == "InGameShopItem_55"
            && cashEntries.Single(x => x.ItemId == 677001).IconName == "InGameShopItem_46"
            && cashEntries.Single(x => x.ItemId == 677002).IconName == "InGameShopItem_47",
            "cash-service packages use semantically matching shipped service artwork rather than weapon artwork");
        Check(cashEntries.Single(x => x.ItemId == 678201) is
                { Name: "City Teleporter 10-Minute Pass", Description: var travelDescription }
            && travelDescription.Contains("teleport device", StringComparison.OrdinalIgnoreCase)
            && !cashEntries.Any(x => x.Name == x.ItemId.ToString()
                || x.Description.Contains("Private-server", StringComparison.OrdinalIgnoreCase)),
            "cash catalog uses recovered client localization without private-server text leaking into the UI");
        Check(_cashShopCatalog.IndexHtml().Contains("All packages are free", StringComparison.Ordinal)
            && _cashShopCatalog.IndexHtml().Contains(_payItems[0].EntryIndex.ToString(), StringComparison.Ordinal),
            "local cash-shop webpage publishes the same free cooked-item catalog");
        using (var cashHttp = new CashShopWebServer(_cashShopCatalog))
        {
            Check(cashHttp.Start(0) && cashHttp.Port > 0, "cash-shop HTTP endpoint binds without Windows URL-ACL privileges");
            using var http = new HttpClient();
            string categoryResponse = await http.GetStringAsync($"http://127.0.0.1:{cashHttp.Port}/ss/000.0000.000/IBSCategory.txt");
            string indexResponse = await http.GetStringAsync($"http://127.0.0.1:{cashHttp.Port}/ss/");
            Check(categoryResponse == CashShopCatalog.CategoryFile() && indexResponse.Contains("Huxley Private Shop"),
                "cash-shop HTTP endpoint serves native catalog paths and the local storefront page");
            // The Wayback mirror is a local download, not part of the repository.
            if (ArchivedWebsite.Root != null)
            {
                using var noRedirect = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
                string web = $"http://127.0.0.1:{cashHttp.Port}{ArchivedWebsite.Prefix}";
                var hostRedirect = await noRedirect.GetAsync(web + "huxley.hangame.com");
                var home = await noRedirect.GetAsync(web + "huxley.hangame.com/");
                string homeHtml = System.Text.Encoding.Latin1.GetString(await home.Content.ReadAsByteArrayAsync());
                var rootRelative = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{cashHttp.Port}/Default.asp");
                rootRelative.Headers.Referrer = new Uri(web + "huxley.hangame.com/GameGuide/Install/Default.asp");
                var rootRelativeResponse = await noRedirect.SendAsync(rootRelative);
                Check(hostRedirect.StatusCode == HttpStatusCode.MovedPermanently
                    && hostRedirect.Headers.Location?.OriginalString == ArchivedWebsite.Prefix + "huxley.hangame.com/"
                    && home.IsSuccessStatusCode && !homeHtml.Contains("http://huxley.hangame.com", StringComparison.OrdinalIgnoreCase)
                    && rootRelativeResponse.IsSuccessStatusCode,
                    "archived official site is served with host links rewritten and root-relative paths resolved by referer");
            }
        }
        var freePackage = cashEntries[0];
        var storedPayItem = _payItemStore.Add(_accountA, freePackage);
        PayItemStore.StoredItem claimedPayItem = new();
        Check(_payItemStore.Get(_accountA).Count == 1
            && _payItemStore.TryTake(_accountA, storedPayItem.StorageSeq, storedPayItem.StorageItemSeq, out claimedPayItem)
            && claimedPayItem.ItemId == freePackage.ItemId && _payItemStore.Get(_accountA).Count == 0,
            "free package purchase persists in account storage and can be claimed exactly once");
        _payItemStore.Restore(_accountA, claimedPayItem);
        Check(_payItemStore.Get(_accountA).Count == 1,
            "failed inventory delivery can restore a claimed paid item without duplication");
        const int payClaimPid = 920094;
        ResetCharacter(payClaimPid);
        Check(_itemManager.TryUsePayItem(_accountA, payClaimPid, claimedPayItem.StorageSeq,
                claimedPayItem.StorageItemSeq, out var transferredPayItem, out var paidInventoryItem)
            && transferredPayItem.ItemId == freePackage.ItemId
            && paidInventoryItem.ItemId == freePackage.ItemId
            && _payItemStore.Get(_accountA).Count == 0
            && _playerStore.GetInventory(payClaimPid, () => []).Count(x => x.ItemId == freePackage.ItemId) == 1,
            "paid-item claim atomically transfers one stored product into character inventory");
        var paidItemData = _itemCatalog.Get(freePackage.ItemId)!;
        Check(paidInventoryItem.Durability == (paidItemData.Repairable ? 100 : 0)
            && (paidInventoryItem.State & 0x02) != 0 == paidItemData.BindOnPickup
            && (paidItemData.ItemLimitTimeType != 1 || paidItemData.ItemLimitDuration <= 0
                || paidInventoryItem.LimitTime > 0),
            "paid-item claim applies cooked durability, pickup binding, and possessed-duration rules");
        var giftedPayItem = _payItemStore.Add(_accountB, freePackage, _ownedA.Name);
        Check(_payItemStore.Get(_accountB) is [var receivedGift]
            && receivedGift.StorageSeq == giftedPayItem.StorageSeq
            && receivedGift.SenderAccountId == _ownedA.Name
            && _characterStore.FindByName(_ownedB.Name.ToUpperInvariant())?.OwnerUserId == _accountB,
            "free gifts resolve character names case-insensitively and persist sender attribution in recipient storage");
        const int newGearPid = 920095;
        ResetCharacter(newGearPid);
        Check(_playerStore.TryBuyItem(newGearPid, _itemCatalog.Get(100101)!, 0, 1,
                out var newGear, out _, out _)
            && newGear is { Durability: 100 },
            "newly purchased repairable equipment starts at full retail durability instead of broken zero");
        Check(_itemCatalog.Get(608001) is { Vehicle: true } && _itemCatalog.Get(100499) is { Vehicle: false },
            "exported item catalog distinguishes HuxleyCityPersonalVehicleData");
        Check(_itemCatalog.Get(677001) is { GeneralValue: 1 } respawnOne
            && _itemCatalog.Get(677002) is { GeneralValue: 10 }
            && _itemCatalog.Get(677003) is { GeneralValue: 8 }
            && _itemCatalog.Get(677004) is { GeneralValue: 5 }
            && respawnOne.ClassName.Contains("PersonalRespawn", StringComparison.Ordinal),
            "exported personal-respawn consumables preserve exact 1/10/8/5 charge values");
        Check(new UseItem { Result = 0, UseType = 3 }.Serialize().SequenceEqual(new byte[] { 79, 0, 3 }),
            "personal-respawn use returns native RESULTTYPE_USEITEM_RESPAWN discriminator 3");
        Check(_itemCatalog.Get(671001) is { ClassName: "HuxleyCityItemData_ResetBattleRecord" }
            && new UseItem { Result = 0, UseType = 6 }.Serialize().SequenceEqual(new byte[] { 79, 0, 6 })
            && new UseItem { Result = 45, UseType = 6 }.Serialize().SequenceEqual(new byte[] { 79, 45 }),
            "battle-record reset preserves the exported item and native use/result discriminators");
        const int sprayPid = 920097;
        ResetCharacter(sprayPid);
        _playerStore.GetInventory(sprayPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 3, ItemId = 664001, Count = 2 },
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 1, ItemId = 200199, Count = 1, Durability = 100 }
        ]);
        Check(_itemCatalog.Get(664001) is { ClassName: "HuxleyCityItemData_ItemColorSpray", CityItemGroup: 1 }
            && _playerStore.TryUseColorSpray(sprayPid, 3, 664001, out var sprayRemaining, out var sprayedArmor)
            && sprayRemaining is { Count: 1 }
            && sprayedArmor is { InventoryIndex: 4, InventoryCell: 1, LimitTime: 0, Spray: 1 }
            && BitConverter.ToInt32(Lunarite.World.ItemManager.SerializeGenericItem(sprayedArmor!), 28) == (sprayedArmor!.Durability | 1 << 16)
            && _playerStore.PruneExpiredInventory(sprayPid, 2_000_000_000).Count == 0
            && _playerStore.GetInventory(sprayPid, () => []).Any(x => x.InventoryIndex == 4 && x.InventoryCell == 1)
            && new UseItem { Result = 0, UseType = 1, ItemData = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray() }
                .Serialize().SequenceEqual(new byte[] { 79, 0, 1 }.Concat(Enumerable.Range(0, 32).Select(i => (byte)i)))
            && Throws<InvalidOperationException>(() => new UseItem { Result = 0, UseType = 1 }.Serialize()),
            "color spray persists ItemArmor.nSpray and returns the native 32-byte replacement item");
        var sprayedNow = _playerStore.GetInventory(sprayPid, () => []).First(x => x.InventoryIndex == 4 && x.InventoryCell == 1);
        Check(BitConverter.ToInt32(_itemManager.BuildRosterArmorPreview(sprayPid), 32 + 28) == sprayedNow.WireDurability
                && _itemManager.EquippedArmorPreview(sprayPid)[1].General3 == sprayedNow.WireDurability
                && sprayedNow.WireDurability >> 16 == 1,
            "character select and customize previews carry the body armor's color spray");
        const int couponPid = 920098;
        ResetCharacter(couponPid);
        _playerStore.GetInventory(couponPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 2, ItemId = 670202, Count = 2 }
        ]);
        Check(_itemCatalog.Get(670202) is { ClassName: "HuxleyCityItemData_HPointCoupon", GeneralValue: 1000,
                CityItemGroup: 14 }
            && _playerStore.TryUseHPointCoupon(couponPid, 2, 670202, 1000, out var couponRemaining, out long couponQt)
            && couponRemaining is { Count: 1 } && couponQt == 1000
            && _playerStore.GetProgress(couponPid).QT == 1000,
            "H-point coupon atomically consumes one table item and grants its exact exported QT value");
        ResetCharacter(ExpandPid);
        Check(!_playerStore.TryBuyItem(ExpandPid, _itemCatalog.Get(604221)!, 48, 1, out _, out _, out _)
            && _equipmentRules.ValidateUseItem(ExpandPid, 48, null, null) == 6
            && _playerStore.GetInventory(ExpandPid, () =>
                [new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 0, ItemId = 604221, Count = 1 }])
                is [{ ItemId: 604221, Count: 1 }],
            "a rejected hidden-cell purchase neither succeeds nor falsely marks a fresh inventory initialized");
    }
}
