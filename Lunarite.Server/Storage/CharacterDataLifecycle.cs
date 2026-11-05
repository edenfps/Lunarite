namespace Lunarite.Storage;

// One authoritative teardown for state whose ownership is unambiguously a character PID.
// Destructive teardown is refused while another subsystem still owns property or leadership.
public sealed class CharacterDataLifecycle
{
    private readonly AuctionStore _auctionStore;
    private readonly BattleResultStore _battleResultStore;
    private readonly BlockStore _blockStore;
    private readonly CharacterStore _characterStore;
    private readonly ClanStore _clanStore;
    private readonly CraftStore _craftStore;
    private readonly DurativeItemStore _durativeItemStore;
    private readonly FriendStore _friendStore;
    private readonly LicenseStore _licenseStore;
    private readonly PersonalBuffStore _personalBuffStore;
    private readonly PlayerStore _playerStore;
    private readonly QuestStore _questStore;
    private readonly TransmitStore _transmitStore;

    private readonly MultiStoreTransaction _multiStoreTransaction;

    public CharacterDataLifecycle(AuctionStore auctionStore, BattleResultStore battleResultStore,
        BlockStore blockStore, CharacterStore characterStore, ClanStore clanStore, CraftStore craftStore,
        DurativeItemStore durativeItemStore, FriendStore friendStore, LicenseStore licenseStore,
        PersonalBuffStore personalBuffStore, PlayerStore playerStore, QuestStore questStore,
        TransmitStore transmitStore, MultiStoreTransaction multiStoreTransaction)
    {
        _auctionStore = auctionStore;
        _battleResultStore = battleResultStore;
        _blockStore = blockStore;
        _characterStore = characterStore;
        _clanStore = clanStore;
        _craftStore = craftStore;
        _durativeItemStore = durativeItemStore;
        _friendStore = friendStore;
        _licenseStore = licenseStore;
        _personalBuffStore = personalBuffStore;
        _playerStore = playerStore;
        _questStore = questStore;
        _transmitStore = transmitStore;
        _multiStoreTransaction = multiStoreTransaction;
    }

    public bool Delete(int pid)
    {
        if (_clanStore.IsMaster(pid) || _auctionStore.HasListings(pid)) return false;
        bool deleted = false;
        _multiStoreTransaction.Run(() =>
        {
            // Publish the roster, inventory/progression, quest, social and auxiliary-store
            // generations as one durable deletion. A crash can therefore replay the whole
            // tombstone instead of resurrecting only part of a character on next startup.
            if (!_clanStore.DeleteCharacter(pid)) return;
            // Legacy/synthetic saves can have PID-owned subsystem data without a roster row;
            // teardown must still remove that unambiguous state.
            _characterStore.Delete(pid);
            _playerStore.DeleteCharacter(pid);
            _licenseStore.Reset(pid);
            _questStore.Delete(pid);
            _transmitStore.DeleteCharacter(pid);
            _personalBuffStore.Reset(pid);
            _durativeItemStore.Reset(pid);
            _craftStore.Reset(pid);
            _battleResultStore.DeleteCharacter(pid);
            _friendStore.DeleteCharacter(pid);
            _blockStore.DeleteCharacter(pid);
            deleted = true;
        });
        return deleted;
    }
}
