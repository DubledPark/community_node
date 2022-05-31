using K2.Core.Util;
using K2Packet;
using K2Packet.DataStruct;
using K2Server.Managers;
using K2Server.Packet.Protocol;
using K2Server.ServerNodes.CommunityNode.Commands;
using K2Server.ServerNodes.CommunityNode.Managers;
using K2Server.ServerNodes.CommunityNode.Models;
using K2Server.ServerNodes.CommunityNode.Thread;
using K2Server.Util;
using Packet.DataStruct;
using SharedLib;
using SharedLib.Data;

namespace K2Server.ServerNodes.CommunityNode.PacketHandler
{
    [LayerClassAttribute(eServerType.Community, typeof(CommunityHandler))]
    public partial class CommunityHandler
    {

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_ServerStatus))]
        static void OnSC_ZMQ_ServerStatus(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_ServerStatus;

            ServerStatusInfoManager.Instance.AddServerStatus(packet.fromServerId, packet.statusPacketValue);
            //ServerStatusInfoManager.Instance.UpdateServerStatus(packet.fromServerId, packet.statusPacketValue);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_ScheduleQuery))]
        static void OnSC_ZMQ_ScheduleQuery(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_ScheduleQuery;

            ScheduleCommunityManager.Instance.SetScheduleInfo(packet.scheduleList);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_ScheduleSave))]
        static void OnSC_ZMQ_ScheduleSave(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_ScheduleSave;
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_ScheduleInfoForCommunity))]
        static void OnCS_ZMQ_ScheduleInfoForCommunity(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_ScheduleInfoForCommunity;

            ScheduleCommunityManager.Instance.ReqScheduleInfo(packet.fromServerId);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_ScheduleControlForCommunity))]
        static void OnCS_ZMQ_ScheduleControlForCommunity(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_ScheduleControlForCommunity;

            ScheduleCommunityManager.Instance.ReqScheduleControl(packet.fromServerId, packet.scheduleId, packet.controlType);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_CheckUserCount))]
        static void OnCS_ZMQ_CheckUserCount(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_CheckUserCount;

            BackgroundJob.Execute(() =>
            {
                WaitingLineManager.Instance.CheckWaitingLine(packet.uId, packet.fromServerId);
            });
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_RemoveWaiting))]
        static void OnCS_ZMQ_RemoveWaiting(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_RemoveWaiting;
            BackgroundJob.Execute(() =>
            {
                WaitingLineManager.Instance.PopWaitingLine(packet.uId);
            });
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_BlackBoardAction))]
        static void OnCS_ZMQ_BlackBoardAction(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_BlackBoardAction;

            var cmd = new BlackBoardActionCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.reqAction = packet.reqAction;
            cmd.userData.Set(packet.userData.uId,
                             packet.userData.charId,
                             packet.userData.name,
                             packet.userData.charClassType,
                             packet.userData.level,
                             packet.userData.zoneId,
                             packet.userData.zoneChannelKey,
                             packet.userData.serverId);

            CommunityWorkerManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_CharacterDelete))]
        static void OnCS_ZMQ_CharacterDelete(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_CharacterDelete;

            var cmd = new CharacterDeleteCheckCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.reqAction = packet.actionType;
            cmd.userData.Set(packet.userData.uId, packet.userData.charId, packet.userData.name,
                             packet.userData.charClassType, packet.userData.level, packet.userData.zoneId,
                             packet.userData.zoneChannelKey, packet.userData.serverId);

            CommunityWorkerManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_UserDead))]
        static void OnCS_ZMQ_UserDead(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_UserDead;
            var cmd = new UserDeadOnCommunityCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.charId = packet.charId;

            CommunityWorkerManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_CommunityInfo))]
        static void OnCS_ZMQ_CommunityInfo(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_CommunityInfo;

            var cmd = new CommunityInfoCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.charId = packet.charId;

            CommunityWorkerManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_PartyActionToCommunity))]
        static void OnCS_ZMQ_PartyActionToCommunity(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_PartyActionToCommunity;

            var cmd = new PartyActionCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.actionType = packet.packetData.actionType;
            cmd.targetCharId = packet.packetData.targetCharId;
            cmd.requesterCharId = packet.requesterCharId;
            cmd.requesterLevel = packet.requesterLevel;
            cmd.requesterName = packet.requesterName;
            cmd.distributionType = packet.packetData.distributionType;
            cmd.lootItemRank = packet.packetData.lootItemRank;

            CommunityWorkerManager.Instance.AddCommand(cmd);

            SLogManager.Instance.InfoLog("[Recv] OnCS_ZMQ_PartyAction() - action({0}), from({1}) - to({2})",
                                            packet.packetData.actionType,
                                            packet.requesterName,
                                            packet.packetData.targetCharId);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_PartyResponse))]
        static void OnCS_ZMQ_PartyResponse(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_PartyResponse;

            var cmd = new PartyResponseCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.actionType = packet.packetData.actionType;
            cmd.responseType = packet.packetData.responseType;
            cmd.requesterCharId = packet.packetData.requesterCharId;
            cmd.responseCharId = packet.responseCharId;
            cmd.distributionType = packet.packetData.distributionType;
            cmd.lootItemRank = packet.packetData.lootItemRank;

            CommunityWorkerManager.Instance.AddCommand(cmd);

            SLogManager.Instance.InfoLog("[Recv] OnCS_ZMQ_PartyResponse() - action({0}), repType({1}), from({2}) - to({3})",
                                            packet.packetData.actionType,
                                            packet.packetData.responseType,
                                            packet.responseCharId,
                                            packet.packetData.requesterCharId);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_PartyCommand))]
        static void OnCS_ZMQ_PartyCommand(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_PartyCommand;

            var cmd = new PartyLeaderCommandCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.commandType = packet.commandType;
            cmd.changedType = packet.changedType;
            cmd.reason = packet.reason;
            cmd.targetCharId = packet.targetCharId;
            cmd.requesterCharId = packet.requesterCharId;
            cmd.lootItemRank = packet.lootItemRank;

            CommunityWorkerManager.Instance.AddCommand(cmd);

            SLogManager.Instance.InfoLog("[Recv] OnCS_ZMQ_PartyCommand() - reqType({0}), from({1})",
                                            packet.changedType,
                                            packet.requesterCharId);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_SearchCharacterName))]
        static void OnCS_ZMQ_SearchCharacterName(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_SearchCharacterName;

            var performerBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(packet.performerCharId);
            if (performerBBModel == null)
                return;

            UserBlackBoardModel findBBModel = null;
            if (string.IsNullOrEmpty(packet.findCharName))
            {
                findBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(packet.findCharId);
            }
            else
            {
                findBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(packet.findCharName);
            }

            var sendPacket = new SC_ZMQ_SearchCharacterName();
            sendPacket.result = findBBModel == null ? ePacketCommonResult.NotFoundCharacter : ePacketCommonResult.Success;
            sendPacket.performerUid = performerBBModel.userInfoData.uId;
            sendPacket.returnType = packet.returnType;

            if (findBBModel != null)
            {
                findBBModel.userInfoData.MakeSearchCharacterProfile(ref sendPacket.profile);
            }

            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, performerBBModel.userInfoData.serverId);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_HiddenBossLoad))]
        static void OnSC_ZMQ_HiddenBossLoad(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_HiddenBossLoad;

            if (packet.result != ePacketCommonResult.Success)
            {
                SLogManager.Instance.ErrorLog($"HiddenBoss failed load - result({packet.result})");
                return;
            }

            var cmd = new HiddenBossLoadCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.hiddenBossList.AddRange(packet.hiddenBossList);
            CommunityWorkerManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_HiddenBossSync))]
        static void OnCS_ZMQ_HiddenBossSync(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_HiddenBossSync;

            var cmd = new HiddenBossSyncCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.conditionDic = packet.conditionDic;
            CommunityWorkerManager.Instance.AddCommand(cmd);
        }

        #region guild (game -> community)
        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildCheck))]
        static void OnCS_ZMQ_GuildCheck(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildCheck;

            var cmd = new GuildCheckReqCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.charId = packet.charId;

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildInfoSync))]
        static void OnCS_ZMQ_GuildInfoSync(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildInfoSync;

            var cmd = new GuildInfoReqCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.guildIdList.AddRange(packet.guildIdList);

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildCreate))]
        static void OnCS_ZMQ_GuildCreate(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildCreate;

            var cmd = new GuildCreateReqCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.requesterInfo.Copy(packet.requesterInfo);

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildJoin))]
        static void OnCS_ZMQ_GuildJoin(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildJoin;

            var cmd = new GuildJoinReqCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.requesterInfo.Copy(packet.requesterInfo);

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildRejoinCoolTimeAction))]
        static void OnCS_ZMQ_GuildRejoinCoolTimeRemove(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildRejoinCoolTimeAction;

            var cmd = new GuildRejoinCoolTimeActionReqCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.requesterInfo.Copy(packet.requesterInfo);
            cmd.actionType = packet.actionType;

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildAction))]
        static void OnCS_ZMQ_GuildAction(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildAction;

            var cmd = new GuildActionReqCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.type = packet.type;
            cmd.requesterInfo.Copy(packet.requesterInfo);

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildExpSync))]
        static void OnCS_ZMQ_GuildExpSync(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildExpSync;

            var cmd = new GuildExpSyncReqCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.charId = packet.charId;
            cmd.addExp = packet.addGuildExp;

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildSearch))]
        static void OnCS_ZMQ_GuildSearch(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildSearch;

            var cmd = new GuildSearchReqCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.type = packet.type;
            cmd.requesterInfo.Copy(packet.requesterInfo);

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildMemberInfo))]
        static void OnCS_ZMQ_GuildMemberInfo(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildMemberInfo;

            var cmd = new GuildMemberInfoReqCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.requesterInfo.Copy(packet.requesterInfo);

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildRecruitInfo))]
        static void OnCS_ZMQ_GuildRecruitInfo(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildRecruitInfo;

            var cmd = new GuildRecruitInfoReqCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.requesterInfo.Copy(packet.requesterInfo);

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildRelationInfo))]
        static void OnCS_ZMQ_GuildRelationInfo(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildRelationInfo;

            var cmd = new GuildRelationInfoReqCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.requesterInfo.Copy(packet.requesterInfo);

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_SaveGuildSkillSlot))]
        static void OnCS_ZMQ_SaveGuildSkillSlot(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_SaveGuildSkillSlot;

            var cmd = new GuildSaveSkillSlotReqCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.guildId = packet.guildId;
            cmd.requesterId = packet.requesterId;
            cmd.skillSlot0 = packet.skillSlot0;
            cmd.skillSlot1 = packet.skillSlot1;
            cmd.skillSlot2 = packet.skillSlot2;

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildPlaceRent))]
        static void OnCS_ZMQ_GuildPlaceRent(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildPlaceRent;

            var cmd = new GuildPlaceRentCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.charId = packet.charId;
            cmd.type = packet.type;
            cmd.cost = packet.cost;

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildPlaceSetZone))]
        static void OnCS_ZMQ_GuildPlaceSetZone(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildPlaceSetZone;

            var cmd = new GuildPlaceSetZoneCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.guildId = packet.guildId;
            cmd.placeZone.Set(packet.placeZone);

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildPlaceGetZone))]
        static void OnCS_ZMQ_GuildPlaceGetZone(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildPlaceGetZone;

            var cmd = new GuildPlaceGetZoneCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.guildId = packet.guildId;

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildInvite))]
        static void OnCS_ZMQ_GuildInvite(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildInvite;

            var cmd = new GuildInviteReqCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.targetName = packet.targetCharName;
            cmd.requesterId = packet.requesterId;
            cmd.guildId = packet.guildId;

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildInviteAccept))]
        static void OnCS_ZMQ_GuildInviteAccept(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildInviteAccept;

            var cmd = new GuildInviteAcceptCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.acceptCharId = packet.acceptCharId;
            cmd.reqCharId = packet.reqCharId;
            cmd.isAccept = packet.isAccept;

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildCheat))]
        static void OnCS_ZMQ_GuildCheat(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildCheat;

            var cmd = new GuildCheatReqCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.requesterInfo.Copy(packet.requesterInfo);
            cmd.cheatCmd = packet.cheatCmd;

            GuildCommunityManager.Instance.AddCommand(cmd);
        }
        #endregion

        #region guild db -> community
        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_GuildLoadQuery))]
        static void OnSC_ZMQ_GuildLoadQuery(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_GuildLoadQuery;

            var cmd = new GuildLoadQueryCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.result = packet.result;
            cmd.loadType = packet.type;
            cmd.ownerId = packet.ownerId;
            cmd.guildId = packet.guildId;
            //cmd.guildName = packet.guildName;
            cmd.requesterInfo.Copy(packet.requesterInfo);
            cmd.guildInfo.Copy(packet.guildInfo);
            cmd.recruitList.AddRange(packet.recruitList);

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_GuildDeleteRelation))]
        static void OnSC_ZMQ_GuildDeleteRelation(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_GuildDeleteRelation;

            var cmd = new GuildDeleteRelationCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.result = packet.result;
            cmd.targetGuildId = packet.targetGuildId;
            cmd.targetGuildName = packet.targetGuildName;
            cmd.relationType = packet.relationType;
            cmd.guildIdList.AddRange(packet.guildIdList);
            cmd.dissolveCharId = packet.dissolveCharId;
            cmd.dissolveCharName = packet.dissolveCharName;

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_GuildSearchQuery))]
        static void OnSC_ZMQ_GuildSearchQuery(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_GuildSearchQuery;

            var cmd = new GuildSearchQueryCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.result = packet.result;
            cmd.searchType = packet.type;
            cmd.requesterInfo.Copy(packet.requesterInfo);
            cmd.guildInfoList.AddRange(packet.guildInfoList);

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_SearchCharacterNameByGuild))]
        static void OnSC_ZMQ_SearchCharacterNameByGuild(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_SearchCharacterNameByGuild;

            var cmd = new GuildCharacterSearchQueryCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.result = packet.result;
            cmd.actionType = packet.actionType;
            cmd.requesterInfo.Copy(packet.requesterInfo);
            cmd.enermyInfo.Copy(packet.enermyInfo);

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_GuildInfoSyncSave))]
        static void OnSC_ZMQ_GuildInfoSyncSave(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_GuildInfoSyncSave;

            var cmd = new GuildInfoSyncSaveCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.result = packet.result;
            cmd.type = packet.type;
            cmd.requestInfo.Copy(packet.requesterInfo);
            cmd.guildInfo.Copy(packet.guildInfo);
            cmd.recruitList.AddRange(packet.recruitList);

            GuildCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_GuildInfoAsyncSave))]
        static void OnSC_ZMQ_GuildInfoAsyncSave(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_GuildInfoAsyncSave;
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_AllGuildInfoQuery))]
        static void OnSC_ZMQ_AllGuildInfoQuery(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_AllGuildInfoQuery;

            GuildCommunityManager.Instance.InitReqAllGuildInfoList(packet.guildInfoList, packet.guildRecruitList);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GuildPlaceTeleportInfo))]
        static void OnCS_ZMQ_GuildPlaceTeleportInfo(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GuildPlaceTeleportInfo;

            var cmd = new GuildPlaceTeleportInfoCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.requesterCharId = packet.charId;
            cmd.teleportGroupID = packet.teleportGroupID;
            
            GuildCommunityManager.Instance.AddCommand(cmd);
            //GuildCommunityManager.Instance.RepGuildPlaceTeleportInfo(cmd.requesterCharId, cmd.teleportGroupID, cmd.fromServerId);
        }
        #endregion

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_AdminMessageLoadQuery))]
        static void OnSC_ZMQ_AdminMessageLoadQuery(BasePacket reqpacket)
        {
            var packet = reqpacket as SC_ZMQ_AdminMessageLoadQuery;

            if(ePacketCommonResult.Success == packet.result)
            {
                var cmd = new AdminMessageLoadQueryCmd();
                cmd.messageList = packet.messageList;
                CommunityWorkerManager.Instance.AddCommand(cmd);
            }

            if(false == packet.isInit)
            {
                //결과 운영툴로 전송 필요
            }

        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_SystemMailSend))]
        static void OnSC_ZMQ_SystemMailSend(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_SystemMailSend;

            var session = SessionManager.Instance.GetSession(packet.sourceSessionId);
            var mailTask = PoolManager.Instance.GetObject<SystemMailThreadTask>();
            mailTask.SetSendResultTask(session, packet.requestKey, packet.orderIdx, packet.failedMaiIds);
            MailManager.Instance.AddTask(mailTask);
        }


        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_ChattingCommunityInfo))]
        static void OnCS_ZMQ_ChattingCommunityInfo(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_ChattingCommunityInfo;

            var cmd = new ChattingCommunityInfoCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.type = eUpdateCommunityType.ChatRequest;
            cmd.charId = packet.charId;

            CommunityWorkerManager.Instance.AddCommand(cmd);
        }


        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_FriendInfo))]
        static void OnSC_ZMQ_FriendInfo(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_FriendInfo;

            var cmd = new FriendInfoCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.charId = packet.requesterCharId;
            cmd.friendList = packet.friendList;
            cmd.actionType = packet.actionType;
            cmd.userChatBlockList = packet.userChatBlockList;
            CommunityWorkerManager.Instance.AddCommand(cmd);

            var userBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(packet.requesterCharId);
            if (userBBModel == null)
            {
                SLogManager.Instance.InfoLog("[Recv] OnSC_ZMQ_FriendInfo() not found userBBModel : requesterCharId({0}), actionType({1})",
                    packet.requesterCharId, packet.actionType);
            }
            else
            {
                switch (cmd.actionType)
                {
                    case eFriendActionType.InfoSync:
                    case eFriendActionType.InfoAll:
                        {
                            var sendPacket = new SC_ZMQ_FriendAction();
                            sendPacket.requesterCharId = packet.requesterCharId;
                            sendPacket.result = packet.dbResult;
                            sendPacket.actionType = packet.actionType;
                            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, userBBModel.userInfoData.serverId);
                        }
                        break;
                }
            }

            SLogManager.Instance.InfoLog("[Recv] OnSC_ZMQ_FriendInfo() - requesterCharId({0})", packet.requesterCharId);
        }
        
        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_FriendAction))]
        static void OnCS_ZMQ_FriendAction(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_FriendAction;
            var cmd = new FriendActionCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.requesterCharId = packet.requesterCharId;
            cmd.targetCharId = packet.targetCharId;
            cmd.targetCharName = packet.targetCharName;
            cmd.actionType = packet.actionType;
            cmd.actionParam = packet.actionParam;
            CommunityWorkerManager.Instance.AddCommand(cmd);
            
            SLogManager.Instance.InfoLog("[Recv] OnCS_ZMQ_FriendAction() - action({0}), from({1}) - to({2})",
                                            packet.actionType,
                                            packet.requesterCharId,
                                            packet.targetCharName);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_FriendActionQuery))]
        static void OnSC_ZMQ_FriendActionQuery(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_FriendActionQuery;
            if (packet.dbResult == ePacketCommonResult.Success)
            {
                var cmd = new FriendActionCmd();
                cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
                cmd.requesterCharId = packet.requesterCharId;
                cmd.targetCharId = packet.targetCharId;
                cmd.targetCharName = packet.targetCharName;
                cmd.charTableId = packet.charTableId;
                cmd.actionType = packet.actionType;
                CommunityWorkerManager.Instance.AddCommand(cmd);
            }
            else
            {
                var userBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(packet.requesterCharId);
                if (userBBModel == null)
                {
                    SLogManager.Instance.InfoLog("[Recv] OnSC_ZMQ_FriendActionQuery() not found userBBModel : charId({0}), actionType({1})",
                        packet.requesterCharId, packet.actionType);
                }
                else
                {
                    var sendPacket = new SC_ZMQ_FriendAction();
                    sendPacket.requesterCharId = packet.requesterCharId;
                    sendPacket.result = packet.dbResult;
                    sendPacket.actionType = packet.actionType;
                    sendPacket.targetCharName = packet.targetCharName;
                    ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, userBBModel.userInfoData.serverId);
                }
            }

            SLogManager.Instance.InfoLog("[Recv] OnSC_ZMQ_FriendActionQuery() - action({0}), from({1}) - to({2})",
                                            packet.actionType,
                                            packet.requesterCharId,
                                            packet.targetCharName);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_FriendSyncSave))]
        static void OnSC_ZMQ_FriendSyncSave(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_FriendSyncSave;

            var cmd = new FriendSyncSaveCmd();

            if (packet.result == ePacketCommonResult.Success)
            {
                cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
                cmd.actionType = packet.actionType;
                cmd.requesterCharId = packet.requesterCharId;
                cmd.friendList.AddRange(packet.friendList);
                cmd.targetCharName = packet.targetCharName;
                CommunityWorkerManager.Instance.AddCommand(cmd);
            }

            if (packet.isRespond)
            {
                var userBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(packet.requesterCharId);
                if (userBBModel != null)
                {
                    var sendPacket = new SC_ZMQ_FriendAction();
                    sendPacket.requesterCharId = packet.requesterCharId;
                    sendPacket.result = packet.result;
                    sendPacket.actionType = packet.actionType;
                    sendPacket.targetCharName = packet.targetCharName;
                    ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, userBBModel.userInfoData.serverId);
                }
            }

            SLogManager.Instance.InfoLog("[Recv] OnOnCS_ZMQ_FriendSyncSave() - action({0}), requestId({1})", packet.actionType, packet.requesterCharId);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_FriendSummon))]
        static void OnCS_ZMQ_FriendSummon(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_FriendSummon;

            var cmd = new FriendSummonCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.requesterCharId = packet.requesterCharId;
            cmd.targetCharId = packet.targetCharId;
            cmd.callMessage = packet.callMessage;
            CommunityWorkerManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_FriendSummonAccept))]
        static void OnCS_ZMQ_FriendSummonAccept(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_FriendSummonAccept;

            var cmd = new FriendSummonAcceptCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.acceptCharId = packet.acceptCharId;
            cmd.reqCharId = packet.reqCharId;
            cmd.isAccept = packet.isAccept;
            CommunityWorkerManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_ChatBlockActionQuery))]
        static void OnSC_ZMQ_ChatBlockActionQuery(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_ChatBlockActionQuery;

            if (packet.result == ePacketCommonResult.Success)
            {
                var cmd = new UserChatBlockStateCmd();
                cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
                cmd.requesterCharId = packet.requesterCharId;
                cmd.targetCharId = packet.targetCharId;
                cmd.targetCharName = packet.targetCharName;
                cmd.actionType = packet.actionType;
                CommunityWorkerManager.Instance.AddCommand(cmd);
            }
            else
            {
                var userBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(packet.requesterCharId);
                if (userBBModel != null)
                {
                    var sendPacket = new SC_ZMQ_ChatBlockAction();
                    sendPacket.requesterCharId = packet.requesterCharId;
                    sendPacket.packetData.result = packet.result;
                    sendPacket.packetData.actionType = packet.actionType;
                    ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, userBBModel.userInfoData.serverId);
                }
            }
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_ChatBlockSyncSave))]
        static void OnSC_ZMQ_ChatBlockSyncSave(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_ChatBlockSyncSave;

            if (packet.result == ePacketCommonResult.Success)
            {
                var cmd = new UserChatBlockSyncSaveCmd();
                cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
                cmd.requesterCharId = packet.requesterCharId;
                cmd.userChatBlockEntity = packet.chatBlockEntity;
                CommunityWorkerManager.Instance.AddCommand(cmd);
            }

            var userBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(packet.requesterCharId);
            if (userBBModel != null)
            {
                var sendPacket = new SC_ZMQ_ChatBlockAction();
                sendPacket.requesterCharId = packet.requesterCharId;
                sendPacket.packetData.result = packet.result;
                sendPacket.packetData.resultList.Add(packet.chatBlockEntity.CopyTo());
                sendPacket.packetData.actionType = packet.actionType;
                ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, userBBModel.userInfoData.serverId);
            }
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_RankingLoadQuery))]
        static void OnSC_ZMQ_RankingLoadQuery(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_RankingLoadQuery;

            if (packet.result == ePacketCommonResult.Success)
            {
                var cmd = new RankingLoadQueryCmd();
                cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
                cmd.refreshKey = packet.refreshKey;
                cmd.lastRefreshKey = packet.lastRefreshKey;
                cmd.needRetry = packet.needRetry;
                cmd.rankingInfoList = packet.rankingInfoList;
                RankingCommunityManager.Instance.AddCommand(cmd);
            }
            
            SLogManager.Instance.InfoLog("[Recv] OnSC_ZMQ_RankingLoadQuery() - refreshKey({0}), needRetry({1}), result({2})", 
                packet.refreshKey, packet.needRetry, packet.result);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_RankingUpdate))]
        static void OnCS_ZMQ_RankingUpdate(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_RankingUpdate;

            var cmd = new RankingUpdateCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.rankingType = packet.rankingType;
            cmd.rankingInfoList = packet.rankingInfoList;
            RankingCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_RankingInfo))]
        static void OnCS_ZMQ_RankingInfo(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_RankingInfo;

            var cmd = new RankingInfoCmd();
            cmd.Init(packet.requestCharId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.rankingType = packet.rankingType;
            cmd.rankingInfoType = packet.rankingInfoType;
            cmd.refreshKey = packet.refreshKey;
            cmd.refreshTime = packet.refreshTime;
            RankingCommunityManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_Cheat_RankingImmediatelyUpdate))]
        static void OnCS_ZMQ_Cheat_RankingImmediatelyUpdate(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_Cheat_RankingImmediatelyUpdate;

            RankingCommunityManager.Instance.RankingUpdate(true);
            var sendPacket = new SC_ZMQ_Cheat_RankingImmediatelyUpdate();
            ServerModule.Instance.Controller.BroadcastZMQ(eServerType.Game, sendPacket);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_RakingDelete))]
        static void OnCS_ZMQ_RakingDelete(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_RakingDelete;

            foreach (var delCharInfo in packet.delCharList)
            {
                RankingCommunityManager.Instance.AddCommand(
                    new RankingCharacterDeleteCmd()
                    {
                        charId = delCharInfo.charId,
                        characterClass = (eCharacterClass)delCharInfo.characterClass
                    }
                );
            }
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_Cheat_RankingDateSummon))]
        static void OnCS_ZMQ_Cheat_RankingDateSummon(BasePacket reqPacket)
        {
            RankingCommunityManager.Instance.lastRankReorganizeCheat = true;
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_CheatSetVaueLongToCommunity))]
        static void OnCS_ZMQ_CheatSetVaueLongToCommunity(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_CheatSetVaueLongToCommunity;

            if (packet.isOnOff)
                K2ReadOnly.Instance.Set(packet.key, packet.value);
            else
                K2ReadOnly.Instance.Reset(packet.key);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_CheatAddExpToGroup))]
        static void OnCS_ZMQ_CheatAddExpToGroup(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_CheatAddExpToGroup;

            var sendPacket = new SC_ZMQ_CheatAddExpToGroup();

            int level = packet.startLevel;
            int percent = 0;
            for (int i = 1; i <= packet.count; ++i)
            {
                string name = string.Format("{0}{1}", packet.name, i);
                var userBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(name);
                if (userBBModel == null)
                    break;

                sendPacket.charId.Add(userBBModel.userInfoData.charId);
                sendPacket.level.Add(level);
                sendPacket.percent.Add(percent);

                percent += packet.incPercent;
                while (percent >= 100)
                {
                    ++level;
                    percent -= 100;
                }
            }

            ServerModule.Instance.Controller.BroadcastZMQ(eServerType.Game, sendPacket);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_CheatSetVaueDoubleToCommunity))]
        static void OnCS_ZMQ_CheatSetVaueDoubleToCommunity(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_CheatSetVaueDoubleToCommunity;

            if (packet.isOnOff)
                K2ReadOnly.Instance.Set(packet.key, packet.value);
            else
                K2ReadOnly.Instance.Reset(packet.key);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_CheatSetVaueStringToCommunity))]
        static void OnCS_ZMQ_CheatSetVaueStringToCommunity(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_CheatSetVaueStringToCommunity;

            if (packet.isOnOff)
                K2ReadOnly.Instance.Set(packet.key, packet.value);
            else
                K2ReadOnly.Instance.Reset(packet.key);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_CheatHiddenBossReset))]
        static void OnCS_ZMQ_CheatHiddenBossReset(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_CheatHiddenBossReset;
            HiddenBossCommunityManager.Instance.ResetSpawnCountAll();
            HiddenBossCommunityManager.Instance.HiddenBossExpire();
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_GameConfigToCommunity))]
        static void OnCS_ZMQ_GameConfigToCommunity(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_GameConfigToCommunity;
            GameConfigManager.Instance.Set(packet.config);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_ObservationInfo))]
        static void OnSC_ZMQ_ObservationInfo(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_ObservationInfo;

            var cmd = new ObservationInfoCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.requestCharId = packet.requestCharId;
            cmd.observationList = packet.observationList;
            cmd.actionType = packet.actionType;
            CommunityWorkerManager.Instance.AddCommand(cmd);

            var userBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(packet.requestCharId);
            if(userBBModel == null)
            {
                SLogManager.Instance.InfoLog("OnSC_ZMQ_ObervationActionInfo() not found userBBModel : charID{(0)}", packet.requestCharId);
            }
            else
            {
                switch(cmd.actionType)
                {
                    case eObservationActionType.InfoAll:
                        {
                            var sendPacket = new SC_ZMQ_ObservationAction();
                            sendPacket.requesterCharId = packet.requestCharId;
                            sendPacket.result = packet.dbResult;
                            sendPacket.actionType = packet.actionType;
                            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, userBBModel.userInfoData.serverId);
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_ObservationActionQuery))]
        static void OnSC_ZMQ_ObservationActionQuery(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_ObservationActionQuery;
            var isConnected = (CommunityWorkerManager.Instance.GetUserBlackBoard(packet.targetCharId) == null) ? false : true;
            if (packet.dbResult == ePacketCommonResult.Success)
            {
                var cmd = new ObservationCmd();
                cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
                cmd.requesterCharId = packet.requesterCharId;
                cmd.targetCharName = packet.targetCharName;
                cmd.targetCharId = packet.targetCharId;
                cmd.targetTableId = packet.targetTableId;
                cmd.actionType = packet.actionType;
                
                CommunityWorkerManager.Instance.AddCommand(cmd);
            }

            var userBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(packet.requesterCharId);
            if (userBBModel == null)
            {
                SLogManager.Instance.InfoLog("OnSC_ZMQ_ObervationActionQuery not found userBBModel : charID{(0)}", packet.requesterCharId);
            }
            else
            {
                var sendPacket = new SC_ZMQ_ObservationAction();
                sendPacket.result = packet.dbResult;
                sendPacket.requesterCharId = packet.requesterCharId;
                sendPacket.targetCharName = packet.targetCharName;
                sendPacket.targetCharId = packet.targetCharId;
                sendPacket.targetTableId = packet.targetTableId;
                sendPacket.actionType = packet.actionType;
                sendPacket.isConnected = isConnected;
                // targetServerType, packet, myServerType, serverID
                ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, userBBModel.userInfoData.serverId);
            }
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_ObservationSave))]
        static void OnSC_ZMQ_ObservationSave(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_ObservationSave;

            var cmd = new ObservationSaveCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.actionType = packet.actionType;
            cmd.requestCharId = packet.reqeustCharId;
            cmd.observationList.AddRange(packet.obEntityList);
            cmd.targetCharName = packet.targetCharName;
            CommunityWorkerManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_ObservationAction))]
        static void OnCS_ZMQ_ObservationAction(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_ObservationAction;
            var cmd = new ObservationCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.requesterCharId = packet.requesterCharId;
            cmd.targetCharId = packet.targetCharId;
            cmd.targetCharName = packet.targetCharName;
            cmd.actionType = packet.actionType;
            CommunityWorkerManager.Instance.AddCommand(cmd);

            SLogManager.Instance.InfoLog("[Recv] OnCS_ZMQ_ObservationAction : actionType({0}), requestCharId({1})", packet.actionType, packet.requesterCharId);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_ObservationMemo))]
        static void OnCS_ZMQ_ObservationMemo(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_ObservationMemo;
            var cmd = new ObservationMemoCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.requestCharId = packet.requestCharId;
            cmd.targetCharId = packet.targetCharId;
            cmd.targetCharName = packet.targetCharName;
            cmd.changeMemo = packet.changeMemo;
            CommunityWorkerManager.Instance.AddCommand(cmd);

            SLogManager.Instance.InfoLog("[Recv] OnCS_ZMQ_ObservationMemo() : requestCharId({0}, targetCharName({1})", packet.requestCharId, packet.targetCharName);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_PvpHistoryTaunt))]
        static void OnCS_ZMQ_PvpHistoryTaunt(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_PvpHistoryTaunt;
            var cmd = new PvpHistoryTauntCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.winnerName = packet.winnerName;
            cmd.loserName = packet.loserName;
            cmd.historySerial = packet.historySerial;
            cmd.charId = packet.charId;
            CommunityWorkerManager.Instance.AddCommand(cmd);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_EventLoadQuery))]
        static void OnSC_ZMQ_EventLoadQuery(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_EventLoadQuery;

            EventCommunityManager.Instance.EventLoad(packet.eventList);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_EventMissionLoadQuery))]
        static void OnSC_ZMQ_EventMissionLoadQuery(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_EventMissionLoadQuery;

            EventCommunityManager.Instance.EventMissionLoad(packet.eventList);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_CharacterDeleteCheck))]
        static void OnCS_ZMQ_CharacterDeleteCheck(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_CharacterDeleteCheck;

            var cmd = new DeleteCheckCharacterCmd();
            cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
            cmd.userData.Set(packet.userData.uId, packet.userData.charId, packet.userData.name,
                             packet.userData.charClassType, packet.userData.level, packet.userData.zoneId,
                             packet.userData.zoneChannelKey, packet.userData.serverId);

            CommunityWorkerManager.Instance.AddCommand(cmd);
        }

        /*
        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_BlockContentsLoadQuery))]
        static void OnSC_ZMQ_BlockContentsLoadQuery(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_BlockContentsLoadQuery;

            BlockContentsCommunityManager.Instance.BlockContentsLoad(packet.blockContentsList);
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(CS_ZMQ_RequestAccept))]
        static void OnCS_ZMQ_RequestAccept(BasePacket reqPacket)
        {
            var packet = reqPacket as CS_ZMQ_RequestAccept;     

            switch (packet.type)
            {
                case eRequestNotiType.GuildInvite:

                    var cmd = new GuildInviteCmd();
                    cmd.Init(packet.uId, packet.fromServerId, packet.serverGroupId, packet.sourceSessionId);
                    
                    //cmd.requesterCharId = packet.requesterCharId;

                    CommunityWorkerManager.Instance.AddCommand(cmd);
                    break;
            }
        }
        */

        //-----------------------------------------------------------
        //                      Test Code 
        //-----------------------------------------------------------
        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_HiveHandShaking))]
        static void OnSC_ZMQ_HiveHandShaking(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_HiveHandShaking;
        }


        [LayerMethodsAttribute(ePacketHandlerLayer.CommunityZmq, typeof(SC_ZMQ_SyncServerGroupInfoToCommunity))]
        static void OnSC_ZMQ_SyncServerGroupInfo(BasePacket reqPacket)
        {
            var packet = reqPacket as SC_ZMQ_SyncServerGroupInfoToCommunity;

            ServerModule.Instance.Controller.SetServerGroupInfo(packet.serverGroupInfoMap);
        }
    }
}
