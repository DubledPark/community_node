using K2Packet.DataStruct;
using K2Server.Packet.Protocol;
using K2Server.ServerNodes.CommunityNode.Managers;
using K2Server.Util;
using RRCommon.Enum;
using SharedLib.Data;

namespace K2Server.ServerNodes.CommunityNode.Commands
{
    public static class GuildCommunityCmdLayer
    {
        #region guild (game -> community)
        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildCheckReqCmd, typeof(GuildCheckReqCmd))]
        public static void OnGuildCheckReqCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildCheckReqCmd;

            GuildCommunityManager.Instance.ReqGuildCheck(cmd.charId, cmd.uId);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildInfoReqCmd, typeof(GuildCheckReqCmd))]
        public static void OnGuildInfoReqCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildInfoReqCmd;

            GuildCommunityManager.Instance.ReqGuildInfo(cmd.guildIdList, cmd.fromServerId);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildCreateReqCmd, typeof(GuildCreateReqCmd))]
        public static void OnGuildCreateReqCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildCreateReqCmd;

            GuildCommunityManager.Instance.ReqGuildCreate(cmd.requesterInfo);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildJoinReqCmd, typeof(GuildJoinReqCmd))]
        public static void OnGuildJoinReqCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildJoinReqCmd;

            GuildCommunityManager.Instance.ReqGuildJoin(cmd.requesterInfo);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildRejoinCoolTimeActionReqCmd, typeof(GuildRejoinCoolTimeActionReqCmd))]
        public static void OnGuildRejoinCoolTimeActionReqCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildRejoinCoolTimeActionReqCmd;

            GuildCommunityManager.Instance.ReqGuildRejoinCoolTimeAction(cmd.requesterInfo, cmd.actionType);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildActionReqCmd, typeof(GuildActionReqCmd))]
        public static void OnGuildActionReqCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildActionReqCmd;

            if (cmd.requesterInfo.paramDic.TryGetValue(eGuildParamType.GmToolAction, out _) == true)
            {
                GuildCommunityManager.Instance.ReqGuildActionByGmTool(cmd.type, cmd.requesterInfo);
            }
            else
            {
                GuildCommunityManager.Instance.ReqGuildAction(cmd.type, cmd.requesterInfo);
            }
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildExpSyncReqCmd, typeof(GuildExpSyncReqCmd))]
        public static void OnGuildExpSyncReqCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildExpSyncReqCmd;

            GuildCommunityManager.Instance.ReqGuildExpSync(cmd.charId, cmd.addExp);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildCheatReqCmd, typeof(GuildCheatReqCmd))]
        public static void OnGuildCheatReqCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildCheatReqCmd;

            GuildCommunityManager.Instance.ReqGuildCheat(cmd.cheatCmd, cmd.requesterInfo);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildSearchReqCmd, typeof(GuildSearchReqCmd))]
        public static void OnGuildSearchReqCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildSearchReqCmd;

            GuildCommunityManager.Instance.ReqGuildSearch(cmd.type, cmd.requesterInfo);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildMemberInfoReqCmd, typeof(GuildMemberInfoReqCmd))]
        public static void OnGuildMemberInfoReqCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildMemberInfoReqCmd;

            GuildCommunityManager.Instance.ReqGuildMemberInfo(cmd.requesterInfo);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildRecruitInfoReqCmd, typeof(GuildRecruitInfoReqCmd))]
        public static void OnGuildRecruitInfoReqCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildRecruitInfoReqCmd;

            GuildCommunityManager.Instance.ReqGuildRecruitInfo(cmd.requesterInfo);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildRelationInfoReqCmd, typeof(GuildRelationInfoReqCmd))]
        public static void OnGuildRelationInfoReqCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildRelationInfoReqCmd;

            GuildCommunityManager.Instance.GuildRelationInfo(cmd.requesterInfo);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildSaveSkillSlotReqCmd, typeof(GuildSaveSkillSlotReqCmd))]
        public static void OnGuildSaveSkillSlotReqCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildSaveSkillSlotReqCmd;

            var result = GuildCommunityManager.Instance.SaveGuildSkillSlot(cmd.guildId, cmd.requesterId, cmd.skillSlot0, cmd.skillSlot1, cmd.skillSlot2);
            if (ePacketCommonResult.Success != result)
            {
                var sendPacket = new SC_ZMQ_SaveGuildSkillSlot();
                sendPacket.guildId = cmd.guildId;
                sendPacket.requesterId = cmd.requesterId;
                ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, cmd.fromServerId);
            }
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildPlaceRentCmd, typeof(GuildPlaceRentCmd))]
        public static void OnGuildPlaceRentCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildPlaceRentCmd;

            var sendPacket = new SC_ZMQ_GuildPlaceRent();
            sendPacket.result = GuildCommunityManager.Instance.GuildPlaceRent(
                cmd.charId, cmd.type, ref sendPacket.entity);
            sendPacket.charId = cmd.charId;
            sendPacket.type = cmd.type;
            sendPacket.cost = cmd.cost;
            sendPacket.uId = cmd.uId;
            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, cmd.fromServerId);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildPlaceSetZoneCmd, typeof(GuildPlaceSetZoneCmd))]
        public static void OnGuildPlaceSetZoneCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildPlaceSetZoneCmd;

            GuildCommunityManager.Instance.SetGuildPlaceZone(cmd.guildId, cmd.placeZone);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildPlaceGetZoneCmd, typeof(GuildPlaceGetZoneCmd))]
        public static void OnGuildPlaceGetZoneCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildPlaceGetZoneCmd;

            var sendPacket = new SC_ZMQ_GuildPlaceSetZone();
            sendPacket.guildId = cmd.guildId;
            sendPacket.placeZone.Set(GuildCommunityManager.Instance.GetGuildPlaceZone(cmd.guildId));
            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, cmd.fromServerId);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildInviteReqCmd, typeof(GuildInviteReqCmd))]
        public static void OnGuildInviteReqCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildInviteReqCmd;

            var sendPacket = new SC_ZMQ_GuildInvite();
            sendPacket.result = GuildCommunityManager.Instance.GuildInviteReq(cmd.targetName, cmd.guildId, cmd.requesterId);
            sendPacket.requesterUid = cmd.uId;
            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, cmd.fromServerId);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildInviteAcceptCmd, typeof(GuildInviteAcceptCmd))]
        public static void OnGuildInviteAcceptCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildInviteAcceptCmd;

            var sendPacket = new SC_ZMQ_GuildInviteAccept();
            sendPacket.result = GuildCommunityManager.Instance.GuildInviteAccept(cmd.acceptCharId, cmd.reqCharId, cmd.isAccept);
            sendPacket.uId = cmd.uId;
            sendPacket.isAccept = cmd.isAccept;
            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, cmd.fromServerId);
        }
        #endregion

        #region guild (db -> community)
        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildLoadQueryCmd, typeof(GuildLoadQueryCmd))]
        public static void OnGuildLoadQueryCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildLoadQueryCmd;

            GuildCommunityManager.Instance.GuildLoad(cmd.loadType, cmd.ownerId, cmd.guildInfo, cmd.recruitList);

            switch (cmd.loadType)
            {
                case eGuildLoadType.CharacterLogin:
                    GuildCommunityManager.Instance.ReqGuildCheck(cmd.ownerId, cmd.uId, false);
                    break;
                case eGuildLoadType.GuildJoin:
                    GuildCommunityManager.Instance.ReqGuildJoin(cmd.requesterInfo, false);
                    break;
                case eGuildLoadType.GuildRelation:
                    GuildCommunityManager.Instance.ReqGuildCheck(cmd.guildInfo.guild.id);
                    break;
                case eGuildLoadType.GuildReqAlliance:
                    GuildCommunityManager.Instance.ReqGuildCheck(cmd.guildInfo.guild.id);
                    GuildCommunityManager.Instance.ReqGuildAction(eGuildActionType.ReqAlliance, cmd.requesterInfo, false);
                    break;
                case eGuildLoadType.GuildAddHostile:
                    GuildCommunityManager.Instance.ReqGuildCheck(cmd.guildInfo.guild.id);
                    GuildCommunityManager.Instance.ReqGuildAction(eGuildActionType.RemoveHostile, cmd.requesterInfo, false);
                    break;
            }
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildDeleteRelationCmd, typeof(GuildDeleteRelationCmd))]
        public static void OnGuildDeleteRelationCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildDeleteRelationCmd;

            GuildCommunityManager.Instance.ReqGuildDeleteRelation(cmd.guildIdList,
                                                                cmd.targetGuildId,
                                                                cmd.relationType,
                                                                cmd.dissolveCharId,
                                                                cmd.dissolveCharName,
                                                                cmd.targetGuildName);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildSearchQueryCmd, typeof(GuildSearchQueryCmd))]
        public static void OnGuildSearchQueryCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildSearchQueryCmd;

            GuildCommunityManager.Instance.RecvZMQGuildSearchQuery(cmd.result, cmd.searchType, cmd.guildInfoList, cmd.requesterInfo);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildCharacterSearchQueryCmd, typeof(GuildCharacterSearchQueryCmd))]
        public static void OnGuildCharacterSearchQueryCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildCharacterSearchQueryCmd;

            GuildCommunityManager.Instance.ReqGuildCharacterSearch(cmd.result, cmd.actionType, cmd.requesterInfo, cmd.enermyInfo);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildInfoSyncSaveCmd, typeof(GuildInfoSyncSaveCmd))]
        public static void OnGuildInfoSyncSaveCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildInfoSyncSaveCmd;

            GuildCommunityManager.Instance.GuildInfoSync(cmd.result, cmd.type, cmd.requestInfo, cmd.guildInfo, cmd.recruitList);
        }

        [GuildCommunityCmdAttribute(eGuildCommunityCmdType.eCommunityGuildTeleportInfoCmd, typeof(GuildPlaceTeleportInfoCmd))]
        public static void OnGuildPlaceTeleportInfoCmd(BaseGuildCommunityCmd baseCmd)
        {
            var cmd = baseCmd as GuildPlaceTeleportInfoCmd;
            
            GuildCommunityManager.Instance.RepGuildPlaceTeleportInfo(cmd.requesterCharId, cmd.teleportGroupID, cmd.fromServerId);
        }
        #endregion
    }
}
