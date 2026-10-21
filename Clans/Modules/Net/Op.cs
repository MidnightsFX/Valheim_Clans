namespace Clans.Net {
    // Message types on the two Clans channels. The numbers are the wire format: append, never renumber.
    // 100-199 travel client -> server, 200-299 server -> client.
    internal enum Op : ushort {
        None = 0,

        // ---- client -> server ----
        Hello = 100,
        Ping = 101,

        CreateClan = 110,
        ConfirmCreateFee = 111,
        Apply = 112,
        WithdrawApplication = 113,
        ResolveApplication = 114,
        Leave = 115,
        Kick = 116,
        SetRank = 117,
        TransferLeader = 118,
        Disband = 119,
        EditInfo = 120,
        EditEmblem = 121,
        EditRanks = 122,
        RequestDetail = 123,
        DonateTreasury = 124,
        WithdrawTreasury = 125,
        CancelCreate = 126,
        ContributeLevel = 127,
        UpgradeLevel = 128,
        BuyFeast = 129,

        ProposeWar = 130,
        RespondWar = 131,
        CancelWarProposal = 132,
        OfferPeace = 133,
        RespondPeace = 134,
        ReportPvpDeath = 135,

        CreateParty = 140,
        InviteParty = 141,
        RespondInvite = 142,
        LeaveParty = 143,
        KickParty = 144,
        SetPartyLeader = 145,
        InviteSquadToRaid = 146,
        LeaveRaid = 147,
        MoveToSquad = 148,
        SetRaidLeader = 149,

        ChatSend = 190,
        ChatPostInvite = 191,
        ChatJoinInvite = 192,
        ChatAttach = 193,
        PlayerStatus = 150,
        MapSharing = 151,
        BountyReport = 152,

        SkillXpBatch = 155,

        OpenStorage = 160,
        StorageSnapshot = 161,
        CloseStorage = 162,
        BuyStorageUpgrade = 163,

        StartActivity = 170,
        CancelActivity = 171,
        BeginActivity = 172,
        ReportKill = 173,
        ReportWorldBossState = 174,
        ReportRaceChests = 176,
        TakeRaceChest = 177,

        AckDelivery = 180,

        // ---- server -> client ----
        Welcome = 200,
        Response = 201,
        Pong = 202,

        DirectoryFull = 210,
        RosterFull = 212,
        MyAccount = 214,
        ClanDetail = 215,

        PartyState = 220,
        Invite = 221,
        InviteClosed = 222,
        PartyPositions = 223,
        ClanPositions = 224,

        SkillXpShare = 225,
        ClanBounties = 226,

        StorageLease = 230,
        StorageResync = 231,
        StorageClosed = 232,

        ActivityState = 240,
        HuntTargetsGlobal = 241,

        RewardGrant = 245,
        Notify = 250,
        ChatMessage = 255,
        ChatAttachments = 256,
    }
}
