using K2Packet.DataStruct;
using K2Packet.Protocol;
using K2Server.Managers;
using K2Server.Packet.Protocol;
using K2Server.ServerNodes.CommunityNode.Managers;
using K2Server.ServerNodes.CommunityNode.Models;
using K2Server.ServerNodes.GameNode.Managers;
using K2Server.Util;
using Packet.DataStruct;
using SharedLib;
using SharedLib.Data;

namespace K2Server.ServerNodes.CommunityNode.Commands
{
    public static class CommunityCmdLayer
    {
        [CommunityCmdAttribute(eCommunityCmdType.eCommunityBlackBoardActionCmd, typeof(BlackBoardActionCmd))]
        public static void OnBlackBoardActionCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as BlackBoardActionCmd;

            var result = CommunityWorkerManager.Instance.ProcessBlackBoard(cmd.reqAction, cmd.userData, cmd.fromServerId, cmd.sourceSessionId, cmd.fromServerGroupId);
            if (result != ePacketCommonResult.Success)
            {
                var sendPacket = new SC_ZMQ_BlackBoardAction();
                sendPacket.result = result;
                sendPacket.uId = cmd.userData.uId;

                ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, cmd.fromServerId);
            }
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityCharacterDeleteCheckCmd, typeof(CharacterDeleteCheckCmd))]
        public static void OnCharacterDeleteCheckCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as CharacterDeleteCheckCmd;

            var sendPacket = new SC_ZMQ_CharacterDelete();
            sendPacket.charId = cmd.userData.charId;
            sendPacket.result = ePacketCommonResult.Success;

            var guildModel = GuildCommunityManager.Instance.GetGuildModel(cmd.userData.charId);
            if (guildModel != null)
            {
                // 길드가 있어서 삭제 불가
                sendPacket.result = ePacketCommonResult.CannotDeleteCharacterByGuildJoined;
            }
            else
            {
                sendPacket.result = CommunityWorkerManager.Instance.ProcessBlackBoard(cmd.reqAction, cmd.userData, cmd.fromServerId, cmd.sourceSessionId, cmd.fromServerGroupId);
            }

            ServerModule.Instance.Controller.SendZMQ(cmd.sourceSessionId, cmd.userData.uId, cmd.fromServerGroupId, cmd.fromServerId, sendPacket);
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityUserDeadOnCommunity, typeof(UserDeadOnCommunityCmd))]
        public static void OnUserDeadOnCommunityCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as UserDeadOnCommunityCmd;
            var userBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(cmd.charId);
            if(null == userBBModel)
            {
                return;
            }

            userBBModel.ResetPartyInviteList(0);
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityInfoCmd, typeof(CommunityInfoCmd))]
        public static void OnCommunityInfoCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as CommunityInfoCmd;

            var sendPacket = new SC_ZMQ_CommunityInfo();
            sendPacket.charId = cmd.charId;
            if (CommunityWorkerManager.Instance.MakeCommunityInfo(ref sendPacket))
            {
                ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, cmd.fromServerId);
            }
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityPartyActionCmd, typeof(PartyActionCmd))]
        public static void OnPartyActionCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as PartyActionCmd;

            var targetBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(cmd.targetCharId);
            if (targetBBModel == null)
            {
                var sendPacket = new SC_ZMQ_PartyAction();
                sendPacket.packetData.result = ePacketCommonResult.NotFoundUserForParty;
                sendPacket.packetData.actionType = cmd.actionType;
                sendPacket.packetData.targetCharId = cmd.targetCharId;
                sendPacket.requesterCharId = cmd.requesterCharId;

                ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, cmd.fromServerId);
                //ServerModule.Instance.GetServerController().SendZMQ(cmd.sourceSessionId, cmd.uId, cmd.fromServerGroupId, cmd.fromServerId, sendPacket);
            }
            else
            {
                var requesterBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(cmd.requesterCharId);
                var result = PartyCommunityManager.Instance.CheckPartyAction(cmd.actionType, cmd.requesterLevel, cmd.distributionType, cmd.lootItemRank, requesterBBModel, targetBBModel);
                if (result != ePacketCommonResult.Success)
                {
                    var errorPacket = new SC_ZMQ_PartyAction();
                    errorPacket.packetData.result = result;
                    errorPacket.packetData.actionType = cmd.actionType;
                    errorPacket.packetData.targetCharId = cmd.targetCharId;
                    errorPacket.requesterCharId = cmd.requesterCharId;

                    ServerModule.Instance.Controller.SendZMQ(eServerType.Game, errorPacket, eServerType.Community, cmd.fromServerId);
                    //ServerModule.Instance.GetServerController().SendZMQ(cmd.sourceSessionId, cmd.uId, cmd.fromServerGroupId, cmd.fromServerId, errorPacket);
                }
            }
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityPartyResponseCmd, typeof(PartyResponseCmd))]
        public static void OnPartyResponseCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as PartyResponseCmd;

            var requesterBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(cmd.requesterCharId);
            if (requesterBBModel != null)
            {
                var responseBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(cmd.responseCharId);
                if (responseBBModel == null || (cmd.responseType == ePartyResponseType.Reject || cmd.responseType == ePartyResponseType.Busy))
                {
                    responseBBModel.RemovePartyInviteList(cmd.requesterCharId);
                    var sendPacket = new SC_ZMQ_PartyResponse();
                    sendPacket.packetData.result = responseBBModel == null ? ePacketCommonResult.NotFoundBBUser : ePacketCommonResult.Success;
                    sendPacket.packetData.actionType = cmd.actionType;
                    sendPacket.packetData.responseType = cmd.responseType;
                    sendPacket.packetData.targetCharId = cmd.responseCharId;
                    sendPacket.requesterCharId = cmd.requesterCharId;

                    ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, requesterBBModel.userInfoData.serverId);
                }
                else
                {
                    var responseMember = new ZmqPartyMemberInfo();
                    responseMember.Set(responseBBModel.userInfoData.charId,
                                       responseBBModel.userInfoData.name,
                                       responseBBModel.userInfoData.charClassType,
                                       responseBBModel.userInfoData.level,
                                       responseBBModel.userInfoData.zoneId,
                                       responseBBModel.userInfoData.zoneChannelKey,
                                       responseBBModel.userInfoData.serverId);

                    PartyCommunityModel partyModel = null;
                    ePacketCommonResult result = ePacketCommonResult.InternalError;
                    if (cmd.actionType == ePartyActionType.Create)
                    {
                        if(responseBBModel.IsParty())
                        {
                            var errorPacket = new SC_ZMQ_PartyResponse();
                            errorPacket.packetData.result = ePacketCommonResult.AlreadyPartyMember;
                            errorPacket.packetData.actionType = cmd.actionType;
                            errorPacket.packetData.responseType = cmd.responseType;
                            errorPacket.packetData.targetCharId = cmd.responseCharId;
                            errorPacket.requesterCharId = cmd.requesterCharId;

                            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, errorPacket, eServerType.Community, responseBBModel.userInfoData.serverId);

                        }

                        responseBBModel.ResetPartyInviteList(requesterBBModel.userInfoData.charId);
                        //여러명에게 보냈을 때 이미 파티가 만들어진 경우
                        if (requesterBBModel.IsParty())
                        {
                            result = PartyCommunityManager.Instance.ProcessPartyInvite(requesterBBModel.partyId, responseMember, out partyModel);
                            if (result == ePacketCommonResult.Success)
                                responseBBModel.SetPartyId(requesterBBModel.partyId);
                        }
                        else
                        {
                            var requesterMember = new ZmqPartyMemberInfo();
                            requesterMember.Set(requesterBBModel.userInfoData.charId,
                                                requesterBBModel.userInfoData.name,
                                                requesterBBModel.userInfoData.charClassType,
                                                requesterBBModel.userInfoData.level,
                                                requesterBBModel.userInfoData.zoneId,
                                                requesterBBModel.userInfoData.zoneChannelKey,
                                                requesterBBModel.userInfoData.serverId);

                            result = PartyCommunityManager.Instance.CreateParty(requesterMember, responseMember, cmd.distributionType, cmd.lootItemRank, out partyModel);
                            if (result == ePacketCommonResult.Success)
                            {
                                requesterBBModel.SetPartyId(partyModel.partyId);
                                responseBBModel.SetPartyId(partyModel.partyId);
                            }
                        }
                    }
                    else if (cmd.actionType == ePartyActionType.Invite)
                    {
                        responseBBModel.ResetPartyInviteList(requesterBBModel.userInfoData.charId);
                        result = PartyCommunityManager.Instance.ProcessPartyInvite(requesterBBModel.partyId, responseMember, out partyModel);
                        if (result == ePacketCommonResult.Success)
                            responseBBModel.SetPartyId(requesterBBModel.partyId);
                    }

                    var sendPacket = new SC_ZMQ_PartyResponse();
                    sendPacket.packetData.result = result;
                    sendPacket.packetData.actionType = cmd.actionType;
                    sendPacket.packetData.responseType = cmd.responseType;
                    sendPacket.packetData.targetCharId = responseMember.charId;
                    sendPacket.packetData.leaderCharId = requesterBBModel.userInfoData.charId;
                    sendPacket.requesterCharId = cmd.requesterCharId;
                    sendPacket.partyId = partyModel != null ? partyModel.partyId : 0;

                    ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, requesterBBModel.userInfoData.serverId);
                    if (requesterBBModel.userInfoData.serverId != responseBBModel.userInfoData.serverId)
                        ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, responseBBModel.userInfoData.serverId);

                    partyModel?.BroadcastingPartyNotify(0, ePartyCommandReason.None);
                }
            }
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityPartyLeaderCommandCmd, typeof(PartyLeaderCommandCmd))]
        public static void OnPartyCommandCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as PartyLeaderCommandCmd;

            var requesterBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(cmd.requesterCharId);
            if (requesterBBModel != null && requesterBBModel.IsParty())
            {
                var partyModel = PartyCommunityManager.Instance.GetPartyModel(requesterBBModel.partyId);
                if (partyModel != null)
                {
                    if (partyModel.leaderCharId == cmd.requesterCharId)
                    {
                        if (cmd.commandType == ePartyLeaderCommand.Distribution)
                        {
                            if (partyModel.UpdateDistribution(cmd.changedType))
                                partyModel.BroadcastingPartyNotify(0, cmd.reason);
                        }
                        else if (cmd.commandType == ePartyLeaderCommand.LeaderChange)
                        {
                            if (partyModel.IsPartyMember(cmd.targetCharId))
                            {
                                if (partyModel.UpdateLeader(cmd.targetCharId))
                                    partyModel.BroadcastingPartyNotify(0, cmd.reason);
                            }
                        }
                        else if (cmd.commandType == ePartyLeaderCommand.Kick)
                        {
                            var targetBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(cmd.targetCharId);
                            if (null == targetBBModel)
                            {
                                PartyCommunityManager.Instance.DelayLeaveParty(partyModel.partyId, cmd.targetCharId);
                            }
                            else if (targetBBModel.partyId == partyModel.partyId)
                            {
                                if (PartyCommunityManager.Instance.LeaveParty(partyModel.partyId, true, targetBBModel))
                                {
                                    SLogManager.Instance.InfoLog("ProcessPartyAction() - [KickMember] : partyId({0}), leader({1}), kickMember({2})",
                                                                    requesterBBModel.partyId,
                                                                    requesterBBModel.userInfoData.name,
                                                                    targetBBModel.userInfoData.name);
                                }
                            }
                        }
                        else if( cmd.commandType == ePartyLeaderCommand.LootOption )
                        {
                            if (partyModel.UpdateLootItemRank(cmd.lootItemRank))
                                partyModel.BroadcastingPartyNotify(0, cmd.reason);
                        }
                    }
                    else
                    {
                        partyModel.BroadcastingPartyNotify(0, cmd.reason);
                    }
                }
            }
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityPartyDelayLeaveCommandCmd, typeof(PartyDelayLeaveCommandCmd))]
        public static void OnPartyDelayLeaveCommandCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as PartyDelayLeaveCommandCmd;

            if(null != CommunityWorkerManager.Instance.GetUserBlackBoard(cmd.charId))
            {
                return;
            }

            if (K2Common.GetDateTime() < cmd.expireTime)
            {
                CommunityWorkerManager.Instance.AddCommand(cmd);
                return;
            }

            PartyCommunityManager.Instance.DelayLeaveParty(cmd.partyId, cmd.charId);

        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityHiddenBossLoadCmd, typeof(HiddenBossLoadCmd))]
        public static void OnHiddenBossLoadCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as HiddenBossLoadCmd;
            HiddenBossCommunityManager.Instance.HiddenBossLoad(cmd.hiddenBossList);
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityHiddenBossSyncCmd, typeof(HiddenBossSyncCmd))]
        public static void OnHiddenBossSyncCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as HiddenBossSyncCmd;
            HiddenBossCommunityManager.Instance.HiddenBossSync(cmd.conditionDic);
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityChattingCommunityInfoCmd, typeof(ChattingCommunityInfoCmd))]
        public static void OnChattingCommunityInfoCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as ChattingCommunityInfoCmd;

            CommunityWorkerManager.Instance.SendZMQChattingCommunityInfo(cmd.type, cmd.charId);
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityFriendInfoCmd, typeof(FriendInfoCmd))]
        public static void OnFriendInfoCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as FriendInfoCmd;

            switch (cmd.actionType)
            {
                case eFriendActionType.InfoAll:
                    FriendCommunityManager.Instance.FriendInfoLoad(cmd.charId, cmd.friendList, cmd.userChatBlockList, eFriendInfoType.All, true);
                    break;

                case eFriendActionType.InfoFriend:
                    FriendCommunityManager.Instance.FriendInfoLoad(cmd.charId, cmd.friendList, cmd.userChatBlockList, eFriendInfoType.Friend, true);
                    break;

                case eFriendActionType.InfoReserve:
                    FriendCommunityManager.Instance.FriendInfoLoad(cmd.charId, cmd.friendList, cmd.userChatBlockList, eFriendInfoType.Reserve, true);
                    break;

                case eFriendActionType.InfoSync:
                    FriendCommunityManager.Instance.FriendInfoLoad(cmd.charId, cmd.friendList, cmd.userChatBlockList, eFriendInfoType.Sync, true);
                    break;

                case eFriendActionType.LoadByChatBlock: 
                    var result = FriendCommunityManager.Instance.FriendInfoLoad(cmd.charId, cmd.friendList, cmd.userChatBlockList, eFriendInfoType.All, true);
                    if (result == ePacketCommonResult.Success)
                        FriendCommunityManager.Instance.ReqUserChatBlockState(cmd.charId, 0, null, eChatBlockActionType.InfoAll);
                    break;

                case eFriendActionType.DeleteSync: 
                    FriendCommunityManager.Instance.DeleteCharacter(cmd.charId, cmd.friendList, cmd.userChatBlockList);
                    break;

                default:
                    SLogManager.Instance.ErrorLog($"Invalid actionType : charId({cmd.charId}), actionType({cmd.actionType})");
                    break;
            }
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityFriendActionCmd, typeof(FriendActionCmd))]
        public static void OnFriendActionCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as FriendActionCmd;
            FriendCommunityManager.Instance.ReqFriendAction(cmd.requesterCharId, cmd.targetCharId
                , cmd.targetCharName, cmd.charTableId, cmd.actionType, cmd.actionParam);
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityUserChatBlockStateCmd, typeof(UserChatBlockStateCmd))]
        public static void OnUserChatBlockStateCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as UserChatBlockStateCmd;
            FriendCommunityManager.Instance.ReqUserChatBlockState(cmd.requesterCharId, cmd.targetCharId, cmd.targetCharName, cmd.actionType);
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityUserChatBlockSyncSaveCmd, typeof(UserChatBlockSyncSaveCmd))]
        public static void OnUserChatBlockSyncSaveCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as UserChatBlockSyncSaveCmd;
            FriendCommunityManager.Instance.ReqUserChatBlockSyncSave(cmd.userChatBlockEntity);
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityFriendSyncSave, typeof(FriendSyncSaveCmd))]
        public static void OnFriendSyncSaveCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as FriendSyncSaveCmd;
            FriendCommunityManager.Instance.ReqFriendSyncSave(cmd.friendList, cmd.actionType, cmd.targetCharName);
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityFriendSummonCmd, typeof(FriendSummonCmd))]
        public static void OnFriendSummonCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as FriendSummonCmd;

            var sendPacket = new SC_ZMQ_FriendSummon();
            sendPacket.result = FriendCommunityManager.Instance.ReqFriendSummon(cmd.requesterCharId, cmd.targetCharId, cmd.callMessage);
            sendPacket.requesterUid = cmd.uId;
            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, cmd.fromServerId);
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityFriendSummonAcceptCmd, typeof(FriendSummonAcceptCmd))]
        public static void OnFriendSummonAcceptCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as FriendSummonAcceptCmd;

            var sendPacket = new SC_ZMQ_FriendSummonAccept();
            sendPacket.result = FriendCommunityManager.Instance.ReqFriendSummonAccept(cmd.acceptCharId, cmd.reqCharId, cmd.isAccept, out var requestBBModel);
            sendPacket.isAccept = cmd.isAccept;
            if (cmd.isAccept)
            {
                sendPacket.targetServerId = requestBBModel.userInfoData.serverId;
                sendPacket.targetZoneId = requestBBModel.userInfoData.zoneId;
                sendPacket.targetZoneChannelKey = requestBBModel.userInfoData.zoneChannelKey; 
            }

            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, cmd.fromServerId);
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityObservationInfoCmd, typeof(ObservationInfoCmd))]
        public static void OnObservationInfoCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as ObservationInfoCmd;

            ObservationCommunityManager.Instance.ObservationInfoFromDB(cmd.requestCharId, cmd.observationList, cmd.actionType);
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityObservationActionCmd, typeof(ObservationCmd))]
        public static void OnObservationActionCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as ObservationCmd;
            ObservationCommunityManager.Instance.ReqObservationAction(cmd.requesterCharId, cmd.targetCharId, cmd.targetCharName, cmd.targetTableId, cmd.actionType);
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityObservationSaveCmd, typeof(ObservationSaveCmd))]
        public static void OnObservationSaveCmd(BaseCommunityCmd baseCmd) 
        {
            var cmd = baseCmd as ObservationSaveCmd;
            ObservationCommunityManager.Instance.ReqObservationSave(cmd.observationList, cmd.actionType, cmd.targetCharName);
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityObservationMemoCmd, typeof(ObservationMemoCmd))]
        public static void OnObservationMemoCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as ObservationMemoCmd;
            ObservationCommunityManager.Instance.reqObservationMemo(cmd.requestCharId, cmd.targetCharId, cmd.targetCharName, cmd.targetTableId, cmd.changeMemo);
        }
       

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityPvpHistoryTauntCmd, typeof(PvpHistoryTauntCmd))]
        public static void OnPvpHistoryTauntCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as PvpHistoryTauntCmd;

            var model = new PvpCommunityModel(cmd.winnerName, cmd.loserName, cmd.historySerial);
            model.TauntProcess();
        }

        [CommunityCmdAttribute(eCommunityCmdType.eCommunityAdminMessageLoadCmd, typeof(AdminMessageLoadQueryCmd))]
        public static void OnAdminMessageLoadQueryCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as AdminMessageLoadQueryCmd;

            AdminMessageManager.Instance.LoadAdminMessage(cmd.messageList);
        }


        [CommunityCmdAttribute(eCommunityCmdType.eCommunityDeleteCheckCharacterCmd, typeof(DeleteCheckCharacterCmd))]
        public static void OnDeleteCheckCharacterCmd(BaseCommunityCmd baseCmd)
        {
            var cmd = baseCmd as DeleteCheckCharacterCmd;

            ePacketCommonResult result = ePacketCommonResult.Success;

            var guildModel = GuildCommunityManager.Instance.GetGuildModel(cmd.userData.charId);
            if (guildModel != null)
            {
                // 길드가 있어서 삭제 불가
                result = ePacketCommonResult.CannotDeleteCharacterByGuildJoined;
            }

            var sendPacket = new SC_ZMQ_CharacterDeleteCheck();
            sendPacket.charId = cmd.userData.charId;
            sendPacket.result = result;

            ServerModule.Instance.Controller.SendZMQ(cmd.sourceSessionId, cmd.userData.uId, cmd.fromServerGroupId, cmd.fromServerId, sendPacket);
        }
    }
}
