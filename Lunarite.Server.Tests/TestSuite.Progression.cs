using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

internal sealed partial class TestSuite
{
    private void ProgressionAuditFixes()
    {
        const int cityTalentPid = 920901;
        ResetCharacter(cityTalentPid);
        Check(_licenseCatalog.Get(703700) is { ClassName: "HuxleyLicenseData_TuneupTalent", DiscountAmount: 5,
                  LicenseType: "LicenseType_CityTalent" }
            && _licenseCatalog.Get(703710) is { ClassName: "HuxleyLicenseData_AuctionTalent", DiscountAmount: 5 }
            && _licenseCatalog.Get(703720) is { ClassName: "HuxleyLicenseData_RepairTalent", DiscountAmount: 5 }
            && _licenseCatalog.Get(713711) is { DiscountAmount: 10 },
            "tier-1 city talents inherit the cooked CityTalent DiscountAmount=5 class default");
        Check(_licenseStore.Grant(cityTalentPid, 703700) && _licenseStore.Grant(cityTalentPid, 703710)
            && _licenseStore.Grant(cityTalentPid, 703720)
            && _licenseStore.TuneupDiscountPercent(cityTalentPid) == 5
            && _licenseStore.AuctionDiscountPercent(cityTalentPid) == 5
            && _licenseStore.RepairDiscountPercent(cityTalentPid) == 5,
            "owning only tier-1 city talents gives the 5-percent fee discount the client displays");
        ResetCharacter(cityTalentPid);

        Check(new ResetLicense { Result = 0, LicensePoints = 70_000, Licenses = [700020] }.Serialize()
                .SequenceEqual(new byte[] { 96, 0, 0xFF, 0xFF, 1, 0x74, 0xAE, 0x0A, 0 })
            && new ResetLicense { Result = 3, LicensePoints = 7, Licenses = [700020] }.Serialize()
                .SequenceEqual(new byte[] { 96, 3 }),
            "server op96 clamps LP to the native word and sends no list on failure");

        const int lowLevelMedalPid = 920902;
        ResetCharacter(lowLevelMedalPid);
        var lowProgress = _playerStore.GetProgress(lowLevelMedalPid);
        lowProgress.BP = PlayerStore.ComputePlayerLevelupGoalBP(25);
        _playerStore.GetInventory(lowLevelMedalPid, () => []).Add(
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 1, ItemId = 609001, Count = 10 });
        Check(!_playerStore.TryDonateCityMedals(lowLevelMedalPid, 609001, 10, 609101, 0)
            && _playerStore.GetProgress(lowLevelMedalPid).AMedalsDonated == 0
            && _playerStore.GetInventory(lowLevelMedalPid, () => []).Single(x => x.ItemId == 609001).Count == 10,
            "medal donations below level 30 are refused and keep the medals instead of stranding them");
        lowProgress.BP = PlayerStore.ComputePlayerLevelupGoalBP(29) + 1;
        Check(_playerStore.TryDonateCityMedals(lowLevelMedalPid, 609001, 10, 609101, 0)
            && _playerStore.TryPromoteRank(lowLevelMedalPid, out _, out byte promotedRank, out _)
            && promotedRank == 1,
            "the same medals donate and promote once the character reaches level 30");
        ResetCharacter(lowLevelMedalPid);
    }
}
