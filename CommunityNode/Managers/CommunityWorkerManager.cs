using K2Packet.DataStruct;
using K2Server.Managers;
using K2Server.Packet.Protocol;
using K2Server.ServerNodes.CommunityNode.Commands;
using K2Server.ServerNodes.CommunityNode.Models;
using K2Server.ServerNodes.CommunityNode.Thread;
using K2Server.ServerNodes.GameNode.Managers;
using K2Server.Util;
using Packet.DataStruct;
using RRCommon.Enum;
using SharedLib;
using SharedLib.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace K2Server.ServerNodes.CommunityNode.Managers
{
    public class CommunityWorkerManager : Singleton<CommunityWorkerManager>
    {
        private ThreadModel_CommunityWorker _workThread = new ThreadModel_CommunityWorker();
        //charId, model
        private ConcurrentDictionary<long, UserBlackBoardModel> _blackBoardDic = new ConcurrentDictionary<long, UserBlackBoardModel>();
        //uid, model
        private ConcurrentDictionary<long, UserBlackBoardModel> _blackBoardUIdDic = new ConcurrentDictionary<long, UserBlackBoardModel>();

        private ConcurrentDictionary<string, UserBlackBoardModel> _blackBoardNameDic = new ConcurrentDictionary<string, UserBlackBoardModel>();
        private Timer _zoneStatusNotifyTimer;
        private Timer _waitingLineTimer;
        public void Init()
        {
            CmdCommunityHandler.CmdBind();
            _workThread.Start(0);
            _waitingLineTimer = new Timer(WaitingLineManager.Instance.WaitingLineNotify, null, TimeSpan.Zero, TimeSpan.FromSeconds(K2Const.WAITINGLINE_REFRESH_TIME));
            this._zoneStatusNotifyTimer = new Timer(OnNotifyZoneUserCountStatusTimer, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        public void AddCommand(BaseCommunityCmd cmd)
        {
            _workThread.AddTaskQueue(cmd);
        }

        public ePacketCommonResult ProcessBlackBoard(eBlackBoardActionType actionType, BlackBoardData data, int fromServerId, string sourceSessionId, int fromServerGroupId)
        {
            switch (actionType)
            {
                case eBlackBoardActionType.Add:
                    {
                        if (_blackBoardDic.ContainsKey(data.charId))
                            return ePacketCommonResult.DuplicatedBBUser;

                        var userBBModel = PoolManager.Instance.GetObject<UserBlackBoardModel>();
                        userBBModel.Set(data);

                        _blackBoardDic.TryAdd(data.charId, userBBModel);
                        _blackBoardNameDic.TryAdd(data.name, userBBModel);
                        _blackBoardUIdDic.TryAdd(data.uId, userBBModel);
                        PartyCommunityManager.Instance.SetExistParty(userBBModel, fromServerId);
                        GuildCommunityManager.Instance.CheckCharacterLogin(data.charId);

                        SLogManager.Instance.InfoLog("[Add BlackBoard] : uId({0}), charId({1})", data.uId, data.charId);

                        // 경계 대상 접속 알림
                        var sendPacket = new CS_ZMQ_SearchObservationQuery();
                        sendPacket.connectCharId = data.charId;
                        sendPacket.gameServerId = data.serverId;
                        ServerModule.Instance.Controller.SendZMQ(eServerType.Database, sendPacket, eServerType.Community);
                    }
                    break;

                case eBlackBoardActionType.Remove:
                    {
                        if (_blackBoardDic.TryRemove(data.charId, out UserBlackBoardModel removeResultUser) == false)
                            return ePacketCommonResult.NotFoundBBUser;

                        _blackBoardNameDic.TryRemove(data.name, out _);
                        _blackBoardUIdDic.TryRemove(data.uId, out _);
                        removeResultUser.ResetPartyInviteList(0);
                        foreach (var userBBModel in _blackBoardDic.Values)
                        {
                            if (userBBModel.IsPartyInviter(data.charId))
                            {
                                userBBModel.RemovePartyInviteList(data.charId);
                                var sendPacket = new SC_ZMQ_PartyResponse();
                                sendPacket.packetData.result = ePacketCommonResult.Success;
                                sendPacket.packetData.actionType = ePartyActionType.Invite;
                                sendPacket.packetData.responseType = ePartyResponseType.Busy;
                                sendPacket.packetData.targetCharId = userBBModel.userInfoData.charId;
                                sendPacket.requesterCharId = data.charId;

                                ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, userBBModel.userInfoData.serverId);
                            }
                        }

                        //PartyCommunityManager.Instance.LeaveParty(removeResultUser.partyId, false, removeResultUser);
                        PartyCommunityManager.Instance.SetDelayLeaveParty(removeResultUser.partyId, removeResultUser.userInfoData.charId, data.uId, fromServerId, fromServerGroupId, sourceSessionId);
                        FriendCommunityManager.Instance.UserDisconnection(removeResultUser);
                        GuildCommunityManager.Instance.CheckCharacterLogout(data.charId);

                        SLogManager.Instance.InfoLog("[Remove BlackBoard] : uId({0}), charId({1})", data.uId, data.charId);
                        removeResultUser.Dispose();
                    }
                    break;

                case eBlackBoardActionType.Move:
                    {
                        if (_blackBoardDic.TryGetValue(data.charId, out UserBlackBoardModel moveResultUser) == false)
                            return ePacketCommonResult.NotFoundBBUser;

                        moveResultUser.Set(data);
                        if (moveResultUser.IsParty())
                        {
                            var partyModel = PartyCommunityManager.Instance.GetPartyModel(moveResultUser.partyId);
                            partyModel.UpdateServerMove(data);

                            var sendPacket = new SC_ZMQ_CommunityInfo();
                            sendPacket.charId = data.charId;
                            partyModel.MakePartyInfoPacket(ref sendPacket.partyInfo);

                            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, fromServerId);
                            partyModel.BroadcastingPartyNotify(0, K2Packet.Protocol.ePartyCommandReason.None);
                        }
                        SLogManager.Instance.InfoLog("[Move BlackBoard] : uId({0}), charId({1})", data.uId, data.charId);
                    }
                    break;

                case eBlackBoardActionType.Update:
                    {
                        if (_blackBoardDic.TryGetValue(data.charId, out UserBlackBoardModel updateResultUser) == false)
                            return ePacketCommonResult.NotFoundBBUser;

                        bool isClassChange = false;
                        if (updateResultUser.userInfoData.charClassType != data.charClassType)
                            isClassChange = true;

                        updateResultUser.Set(data);
                        if (updateResultUser.IsParty())
                        {
                            var partyModel = PartyCommunityManager.Instance.GetPartyModel(updateResultUser.partyId);
                            partyModel.UpdateMemberInfo(data);
                            partyModel.BroadcastingPartyNotify(0, K2Packet.Protocol.ePartyCommandReason.None);
                        }

                        if (isClassChange)
                        {
                            GuildCommunityManager.Instance.ReqGuildMemberClassChange(data.charId, data.charClassType);
                            FriendCommunityManager.Instance.ReqFriendClassChange(data.charId, data.charClassType);
                            GuildCommunityManager.Instance.ReqGuildRecruitMemberClassChange(data.charId, data.charClassType);
                        }

                        SLogManager.Instance.InfoLog("[Update BlackBoard] : uId({0}), charId({1})", data.uId, data.charId);
                    }
                    break;

                case eBlackBoardActionType.CharacterDelete:
                    {
                        // 접속 안한 친구도 지워야 하기 때문에 일단 블랙보드 데이터를 등록한다
                        if (_blackBoardDic.TryGetValue(data.charId, out var userBBModel) == false)
                        {
                            userBBModel = PoolManager.Instance.GetObject<UserBlackBoardModel>();
                            userBBModel.Set(data);

                            _blackBoardDic.TryAdd(data.charId, userBBModel);
                            _blackBoardNameDic.TryAdd(data.name, userBBModel);
                            _blackBoardUIdDic.TryAdd(data.uId, userBBModel);
                        }

                        userBBModel.delayedEvent.AddEvent(eDelayedEventKindType.BlackBoardCharacterDelete, eBlackBoardEventType.Friend);
                        {
                            var sendPacket = new CS_ZMQ_FriendInfo();
                            sendPacket.actionType = eFriendActionType.DeleteSync;
                            sendPacket.requesterCharId = data.charId;
                            ServerModule.Instance.Controller.SendZMQ(eServerType.Database, sendPacket, eServerType.Community);
                        }

                        userBBModel.delayedEvent.AddEvent(eDelayedEventKindType.BlackBoardCharacterDelete, eBlackBoardEventType.GuildEnermy);
                        {
                            var sendPacket = new CS_ZMQ_GuildDeleteRelation();
                            sendPacket.targetGuildId = data.charId;
                            sendPacket.relationType = eGuildRelationType.Enermy;
                            ServerModule.Instance.Controller.SendZMQ(eServerType.Database, sendPacket, eServerType.Community);
                        }

                        RankingCommunityManager.Instance.AddCommand(
                            new RankingCharacterDeleteCmd()
                            {
                                charId = data.charId,
                                characterClass = (eCharacterClass)DataManager.Get<LoaderClassData>().GetBaseClassId((int)data.charClassType)
                            });

                        SLogManager.Instance.InfoLog("[Delete BlackBoard] : uId({0}), charId({1})", data.uId, data.charId);
                    }
                    break;

                default:
                    SLogManager.Instance.ErrorLog($"Unknown action type : ({actionType})");
                    break;
            }

            return ePacketCommonResult.Success;
        }

        public ePacketCommonResult RemoveUserData(long targetCharId)
        {
            if (_blackBoardDic.TryRemove(targetCharId, out var removeResultUser))
            {
                _blackBoardNameDic.TryRemove(removeResultUser.userInfoData.name, out var tempResultUser);
                _blackBoardUIdDic.TryRemove(removeResultUser.userInfoData.uId, out UserBlackBoardModel removeUser);

                removeResultUser.Dispose();
                return ePacketCommonResult.Success;
            }

            return ePacketCommonResult.InternalError;
        }

        public bool MakeCommunityInfo(ref SC_ZMQ_CommunityInfo packet)
        {
            if (_blackBoardDic.ContainsKey(packet.charId) == false)
            {
                SLogManager.Instance.ErrorLog($"Not found blackboard user !! ({packet.charId})");
                return false;
            }

            //파티
            if (_blackBoardDic[packet.charId].IsParty())
            {
                var partyModel = PartyCommunityManager.Instance.GetPartyModel(_blackBoardDic[packet.charId].partyId);
                if (partyModel != null)
                {
                    partyModel.MakePartyInfoPacket(ref packet.partyInfo);
                }
            }

            //to do : 길드

            return true;
        }

        public UserBlackBoardModel GetUserBlackBoard(long charId)
        {
            _blackBoardDic.TryGetValue(charId, out UserBlackBoardModel result);
            return result;
        }

        public UserBlackBoardModel GetUserBlackBoard(string name)
        {
            _blackBoardNameDic.TryGetValue(name, out UserBlackBoardModel result);
            return result;
        }
        public UserBlackBoardModel GetUserBlackBoardByUid(long uid)
        {
            _blackBoardUIdDic.TryGetValue(uid, out UserBlackBoardModel result);
            return result;
        }

        public void Check()
        {
            foreach (var userBBModel in _blackBoardDic.Values)
                userBBModel.CheckDelayedEvent();
        }

        public int GetBlockBoardCount()
        {
            if (_blackBoardDic != null)
            {
                return _blackBoardDic.Count;
            }

            return 0;
        }

        public void SendZMQChattingCommunityInfo(eUpdateCommunityType type, long charId)
        {
            var sendPacket = new SC_ZMQ_ChattingCommunityInfo();
            sendPacket.updateType = type;
            sendPacket.charId = charId;
            sendPacket.result = ePacketCommonResult.Success;
            var userBBModel = GetUserBlackBoard(charId);
            if (null == userBBModel)
            {
                sendPacket.result = ePacketCommonResult.NotFoundBBUser;
                ServerModule.Instance.Controller.SendZMQ(eServerType.Chatting, sendPacket, eServerType.Community);
                return;
            }
            sendPacket.partyId = PartyCommunityManager.Instance.GetMyPartyId(charId);
            sendPacket.guildId = GuildCommunityManager.Instance.GetMyGuildId(charId);
            sendPacket.guildName = GuildCommunityManager.Instance.GetMyGuildName(charId);
            sendPacket.userChatBlockList = FriendCommunityManager.Instance.GetUserChatBlockList(charId);
            ServerModule.Instance.Controller.SendZMQ(eServerType.Chatting, sendPacket, eServerType.Community);
        }

        private void OnNotifyZoneUserCountStatusTimer(object state)
        {
            var notify = new CS_ZMQ_NotifyZoneUserCountStatus();

            this.SetZoneUserCountStatusList(ref notify);
            if (notify.statusMap.Count > 0)
            {
                ServerModule.Instance.Controller
                    .BroadcastZMQ(eServerType.Gateway, notify);
            }
        }

        private void SetZoneUserCountStatusList(ref CS_ZMQ_NotifyZoneUserCountStatus packet)
        {
            var groups = this._blackBoardNameDic.Values
                .GroupBy(e =>
                {
                    long key = ((long)e.userInfoData.serverId << 32) | (long)e.userInfoData.zoneId;

                    return key;
                });

            foreach (IGrouping<long, UserBlackBoardModel> group in groups)
            {
                int userCount = group.Count();
                
                var userInfoData = group.First().userInfoData;
                if (packet.statusMap.ContainsKey(userInfoData.zoneId) == false)
                {
                    packet.statusMap.Add(userInfoData.zoneId, new List<ZoneUserCountStatus>());
                }

                packet.statusMap[userInfoData.zoneId].Add(new ZoneUserCountStatus
                {
                    gameServerId = userInfoData.serverId,
                    userCount = userCount,
                });
            }
        }
    }
}
