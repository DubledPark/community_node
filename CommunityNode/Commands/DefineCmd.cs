using K2Packet.DataStruct;
using K2Packet.Protocol;
using K2Server.Database.Entities;
using K2Server.Packet.Protocol;
using K2Server.Util;
using Packet.DataStruct;
using RRCommon.Enum;
using SharedLib.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace K2Server.ServerNodes.CommunityNode.Commands
{
    public class BlackBoardActionCmd : BaseCommunityCmd
    {
        public eBlackBoardActionType reqAction;
        public BlackBoardData userData = new BlackBoardData();

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityBlackBoardActionCmd; } }
    }

    public class CharacterDeleteCheckCmd : BaseCommunityCmd
    {
        public eBlackBoardActionType reqAction;
        public BlackBoardData userData = new BlackBoardData();
        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityCharacterDeleteCheckCmd; } }
    }

    public class UserDeadOnCommunityCmd : BaseCommunityCmd
    {
        public long charId;
        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityUserDeadOnCommunity; } }
    }

    public class DeleteCheckCharacterCmd : BaseCommunityCmd
    {
        public BlackBoardData userData = new BlackBoardData();
        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityDeleteCheckCharacterCmd; } }
    }

    public class PartyActionCmd : BaseCommunityCmd
    {
        public ePartyActionType actionType;
        public long targetCharId;
        public long requesterCharId;
        public int requesterLevel;
        public string requesterName;
        public ePartyDistributionType distributionType;
        public eItemRank lootItemRank;

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityPartyActionCmd; } }
    }

    public class PartyResponseCmd : BaseCommunityCmd
    {
        public ePartyActionType actionType;
        public ePartyResponseType responseType;
        public long requesterCharId;
        public long responseCharId;
        public ePartyDistributionType distributionType;
        public eItemRank lootItemRank;

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityPartyResponseCmd; } }
    }

    public class PartyLeaderCommandCmd : BaseCommunityCmd
    {
        public ePartyLeaderCommand commandType;
        public ePartyDistributionType changedType;
        public long targetCharId;
        public long requesterCharId;
        public ePartyCommandReason reason;
        public eItemRank lootItemRank;

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityPartyLeaderCommandCmd; } }
    }

    public class PartyDelayLeaveCommandCmd : BaseCommunityCmd
    {
        public DateTime expireTime;
        public long partyId;
        public long charId;
        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityPartyDelayLeaveCommandCmd; } }
    }

    public class CommunityInfoCmd : BaseCommunityCmd
    {
        public long charId;

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityInfoCmd; } }
    }

    public class GuildCheckReqCmd : BaseGuildCommunityCmd
    {
        public long charId;

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildCheckReqCmd; } }
    }

    public class GuildInfoReqCmd : BaseGuildCommunityCmd
    {
        public List<long> guildIdList = new List<long>();

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildInfoReqCmd; } }
    }

    public class GuildCreateReqCmd : BaseGuildCommunityCmd
    {
        public GuildRequesterData requesterInfo = new GuildRequesterData();

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildCreateReqCmd; } }
    }

    public class GuildJoinReqCmd : BaseGuildCommunityCmd
    {
        public GuildRequesterData requesterInfo = new GuildRequesterData();

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildJoinReqCmd; } }
    }

    public class GuildRejoinCoolTimeActionReqCmd : BaseGuildCommunityCmd
    {
        public GuildRequesterData requesterInfo = new GuildRequesterData();
        public eGuildRejoinCoolTimeActionType actionType;

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildRejoinCoolTimeActionReqCmd; } }
    }

    public class GuildActionReqCmd : BaseGuildCommunityCmd
    {
        public eGuildActionType type;
        public GuildRequesterData requesterInfo = new GuildRequesterData();

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildActionReqCmd; } }
    }

    public class GuildExpSyncReqCmd : BaseGuildCommunityCmd
    {
        public long charId;
        public long addExp;

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildExpSyncReqCmd; } }
    }

    public class GuildCheatReqCmd : BaseGuildCommunityCmd
    {
        public GuildRequesterData requesterInfo = new GuildRequesterData();
        public eCheatCommand cheatCmd;

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildCheatReqCmd; } }
    }

    public class GuildSearchReqCmd : BaseGuildCommunityCmd
    {
        public eGuildSearchType type;
        public GuildRequesterData requesterInfo = new GuildRequesterData();

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildSearchReqCmd; } }
    }

    public class GuildMemberInfoReqCmd : BaseGuildCommunityCmd
    {
        public GuildRequesterData requesterInfo = new GuildRequesterData();

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildMemberInfoReqCmd; } }
    }

    public class GuildRecruitInfoReqCmd : BaseGuildCommunityCmd
    {
        public GuildRequesterData requesterInfo = new GuildRequesterData();

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildRecruitInfoReqCmd; } }
    }

    public class GuildRelationInfoReqCmd : BaseGuildCommunityCmd
    {
        public GuildRequesterData requesterInfo = new GuildRequesterData();

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildRelationInfoReqCmd; } }
    }

    public class GuildSaveSkillSlotReqCmd : BaseGuildCommunityCmd
    {
        public long guildId;
        public long requesterId;
        public int skillSlot0;
        public int skillSlot1;
        public int skillSlot2;

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildSaveSkillSlotReqCmd; } }
    }

    public class GuildPlaceRentCmd : BaseGuildCommunityCmd
    {
        public long charId;
        public eGuildPlaceActionType type;
        public long cost;

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildPlaceRentCmd; } }
    }

    public class GuildPlaceSetZoneCmd : BaseGuildCommunityCmd
    {
        public long guildId;
        public GuildPlaceZone placeZone = new GuildPlaceZone();

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildPlaceSetZoneCmd; } }
    }

    public class GuildPlaceGetZoneCmd : BaseGuildCommunityCmd
    {
        public long guildId;

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildPlaceGetZoneCmd; } }
    }

    public class GuildInviteReqCmd : BaseGuildCommunityCmd
    {
        public string targetName;
        public long guildId;
        public long requesterId;

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildInviteReqCmd; } }
    }

    public class GuildInviteAcceptCmd : BaseGuildCommunityCmd
    {
        public long acceptCharId;
        public long reqCharId;
        public bool isAccept;

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildInviteAcceptCmd; } }
    }

    public class GuildLoadQueryCmd : BaseGuildCommunityCmd
    {
        public ePacketCommonResult result;
        public eGuildLoadType loadType;
        public long ownerId;
        public long guildId;
        public GuildRequesterData requesterInfo = new GuildRequesterData();
        public GuildInfoData guildInfo = new GuildInfoData();
        public List<GuildRecruitEntity> recruitList = new List<GuildRecruitEntity>();
        public eGuildJoinType joinType;

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildLoadQueryCmd; } }
    }

    public class GuildDeleteRelationCmd : BaseGuildCommunityCmd
    {
        public ePacketCommonResult result;
        public long targetGuildId;
        public string targetGuildName;
        public eGuildRelationType relationType;
        public List<long> guildIdList = new List<long>();
        public long dissolveCharId;
        public string dissolveCharName;

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildDeleteRelationCmd; } }
    }

    public class GuildSearchQueryCmd : BaseGuildCommunityCmd
    {
        public ePacketCommonResult result;
        public eGuildSearchType searchType;
        public GuildRequesterData requesterInfo = new GuildRequesterData();
        public List<GuildSearchInfoPacket> guildInfoList = new List<GuildSearchInfoPacket>();

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildSearchQueryCmd; } }
    }

    public class GuildCharacterSearchQueryCmd : BaseGuildCommunityCmd
    {
        public ePacketCommonResult result;
        public eGuildActionType actionType;
        public GuildRequesterData requesterInfo = new GuildRequesterData();
        public GuildEnermyInfoPacket enermyInfo = new GuildEnermyInfoPacket();

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildCharacterSearchQueryCmd; } }
    }

    public class GuildInfoSyncSaveCmd : BaseGuildCommunityCmd
    {
        public ePacketCommonResult result;
        public eGuildInfoSyncSaveType type;
        public GuildRequesterData requestInfo = new GuildRequesterData();
        public GuildInfoData guildInfo = new GuildInfoData();
        public List<GuildRecruitEntity> recruitList = new List<GuildRecruitEntity>();

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildInfoSyncSaveCmd; } }
    }

    public class GuildInviteCmd : BaseGuildCommunityCmd
    {
        public long requesterCharId;
        public string vvv;

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildInviteCmd; } }
    }

    public class GuildPlaceTeleportInfoCmd : BaseGuildCommunityCmd
    {
        public long requesterCharId;
        public int teleportGroupID;

        public override eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eCommunityGuildTeleportInfoCmd; } }
    }

    public class FriendInfoCmd : BaseCommunityCmd
    {
        public long charId;
        public List<FriendEntity> friendList;
        public List<UserChatBlockEntity> userChatBlockList;
        public eFriendActionType actionType;
        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityFriendInfoCmd; } }
    }

    public class FriendActionCmd : BaseCommunityCmd
    {
        public long requesterCharId;
        public long targetCharId;
        public string targetCharName;
        public int charTableId;
        public string actionParam;
        public eFriendActionType actionType;
        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityFriendActionCmd; } }
    }

    public class FriendSyncSaveCmd : BaseCommunityCmd
    {
        public eFriendActionType actionType;
        public long requesterCharId;
        public List<FriendEntity> friendList = new List<FriendEntity>();
        public string targetCharName;
        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityFriendSyncSave; } }
    }

    public class FriendSummonCmd : BaseCommunityCmd
    {
        public long requesterCharId;
        public long targetCharId;
        public string callMessage;

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityFriendSummonCmd; } }
    }

    public class FriendSummonAcceptCmd : BaseCommunityCmd
    {
        public long acceptCharId;
        public long reqCharId;
        public bool isAccept;

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityFriendSummonAcceptCmd; } }
    }

    public class UserChatBlockStateCmd : BaseCommunityCmd
    {
        public long requesterCharId;
        public long targetCharId;
        public string targetCharName;
        public eChatBlockActionType actionType;

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityUserChatBlockStateCmd; } }
    }

    public class UserChatBlockSyncSaveCmd : BaseCommunityCmd
    {
        public long requesterCharId;
        public UserChatBlockEntity userChatBlockEntity;

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityUserChatBlockSyncSaveCmd; } }
    }

    public class ObservationInfoCmd : BaseCommunityCmd
    {
        public long requestCharId;
        public List<ObservationEntity> observationList;
        public eObservationActionType actionType;

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityObservationInfoCmd; } }
    }

    public class ObservationCmd : BaseCommunityCmd
    {
        public long requesterCharId;
        public string targetCharName;
        public long targetCharId;
        public int targetTableId;
        public eObservationActionType actionType;

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityObservationActionCmd; } }
    }

    public class ObservationSaveCmd : BaseCommunityCmd
    {
        public eObservationActionType actionType;
        public long requestCharId;
        public List<ObservationEntity> observationList = new List<ObservationEntity>();
        public string targetCharName;

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityObservationSaveCmd; } }
    }

    public class ObservationMemoCmd : BaseCommunityCmd
    {
        public long requestCharId;
        public string targetCharName;
        public long targetCharId;
        public int targetTableId;
        public string changeMemo;

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityObservationMemoCmd; } }
    }

    public class PvpHistoryTauntCmd : BaseCommunityCmd
    {
        public string winnerName;
        public string loserName;
        public long historySerial;
        public long charId;

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityPvpHistoryTauntCmd; } }
    }

    public class HiddenBossLoadCmd : BaseCommunityCmd
    {
        public List<HiddenBossEntity> hiddenBossList = new List<HiddenBossEntity>();

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityHiddenBossLoadCmd; } }
    }

    public class HiddenBossSyncCmd : BaseCommunityCmd
    {
        public Dictionary<int, int> conditionDic = new Dictionary<int, int>();

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityHiddenBossSyncCmd; } }
    }

    public class ChattingCommunityInfoCmd : BaseCommunityCmd
    {
        public long charId;
        public eUpdateCommunityType type;

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityChattingCommunityInfoCmd; } }
    }


    public class AdminMessageLoadQueryCmd : BaseCommunityCmd
    {
        public List<AdminMessageEntity> messageList;

        public override eCommunityCmdType cmdId { get { return eCommunityCmdType.eCommunityAdminMessageLoadCmd; } }
    }

    public class RankingUpdateCmd : BaseRankingCmd
    {
        public eRankingType rankingType;
        public List<RankingPacket> rankingInfoList = new List<RankingPacket>();

        public override eRankingCmdType cmdId { get { return eRankingCmdType.RankingUpdateCmd; } }
    }

    public class RankingGuildUpdateCmd : BaseRankingCmd
    {
        public long charId;
        public long guildId;
        public int crestId;
        public string guildName;
        public bool isRemove;

        public override eRankingCmdType cmdId { get { return eRankingCmdType.RankingGuildUpdate; } }
    }

    public class RankingGuildDeleteCmd : BaseRankingCmd
    {
        public long guildId;

        public override eRankingCmdType cmdId { get { return eRankingCmdType.RankingGuildDeleteCmd; } }
    }

    public class RankingCharacterDeleteCmd : BaseRankingCmd
    {
        public long charId;
        public long guildId;
        public eCharacterClass characterClass;
            
        public override eRankingCmdType cmdId { get { return eRankingCmdType.RankingCharacterDeleteCmd; } }
    }

    public class RankingInfoCmd : BaseRankingCmd
    {
        public eRankingType rankingType;
        public eRankingInfoType rankingInfoType;
        public int refreshKey;
        public DateTime refreshTime;

        public override eRankingCmdType cmdId { get { return eRankingCmdType.RankingInfoCmd; } }
    }

    public class RankingLoadQueryCmd : BaseRankingCmd
    {
        public ePacketCommonResult result;
        public int refreshKey;
        public int lastRefreshKey;
        public bool needRetry;
        public List<RankingEntity> rankingInfoList;

        public override eRankingCmdType cmdId { get { return eRankingCmdType.RankingLoadQueryCmd; } }
    }
}
