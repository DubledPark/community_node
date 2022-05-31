using K2Packet;
using K2Packet.DataStruct;
using K2Packet.Protocol;
using K2Server.Database.Entities;
using K2Server.Managers;
using K2Server.Packet.Protocol;
using K2Server.ServerNodes.CommunityNode.Commands;
using K2Server.ServerNodes.CommunityNode.Managers;
using K2Server.ServerNodes.CommunityNode.Thread;
using K2Server.ServerNodes.GameNode.Helper;
using K2Server.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packet.Protocol;
using RRCommon.Enum;
using SharedLib;
using SharedLib.Data;
using System;
using System.Collections.Generic;

namespace K2Server.ServerNodes.CommunityNode.PacketHandler
{
    [LayerClassAttribute(eServerType.Community, typeof(WebHandler))]
    public partial class WebHandler
    {
        [LayerMethodsAttribute(ePacketHandlerLayer.Community, typeof(CS_Web_UserOnlineCheck))]
        static void OnCS_Web_UserOnlineCheck(Session session, BasePacket reqPacket)
        {
            var packet = reqPacket as CS_Web_UserOnlineCheck;
            var result = ePacketCommonResult.Success;

            // 확인용 로그 추가
            SLogManager.Instance.InfoLog($"OnCS_Web_UserOnlineCheck(). packet({JsonConvert.SerializeObject(packet)})");

            #region [로그] 81004 - 운영툴 서버 요청
            SLogManager.Instance.GameLog(81004, null
                , new GameLogParameter("serverGroupId", "" + ServerModule.Instance.GetMyServerGroupId())
                , new GameLogParameter("serverId", "" + ServerModule.Instance.GetMyServerId())
                , new GameLogParameter("requestType", "UserOnlineCheck")
                , new GameLogParameter("packet", "" + JsonConvert.SerializeObject(packet))
                , new GameLogParameter("result", "" + result.ToString())
                );
            #endregion

            var userBlackBoardModel = CommunityWorkerManager.Instance.GetUserBlackBoardByUid(packet.uId);

            result = (userBlackBoardModel != null) ? ePacketCommonResult.Success : ePacketCommonResult.FailedLogin;

            var resultPacket = new SC_Web_UserOnlineCheck();
            resultPacket.result = result;
            resultPacket.requestKey = packet.requestKey;
            session.SendPacket(PacketBuildWrapper.Build(resultPacket));

            #region [로그] 81006 - 유저 온라인 여부 확인 결과
            SLogManager.Instance.GameLog(81006, null
                , new GameLogParameter("uId", "" + packet.uId)
                , new GameLogParameter("charId", "0")
                , new GameLogParameter("serverGroupId", "" + ServerModule.Instance.GetMyServerGroupId())
                , new GameLogParameter("serverId", "" + ServerModule.Instance.GetMyServerId())
                , new GameLogParameter("updateTime", "" + DateTime.Now.ToLongTimeString())
                , new GameLogParameter("result", "" + result.ToString())
                );
            #endregion
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.Community, typeof(CS_Web_SystemMailSend))]
        static void OnCS_Web_SystemMailSend(Session session, BasePacket reqPacket)
        {
            var packet = reqPacket as CS_Web_SystemMailSend;
            var result = ePacketCommonResult.Success;
            var now = DateTime.Now;
            var sendMailList = new List<MailSendPacketData>();
            int mailType = packet.mailType;


            DateTime reservedDate;
            DateTime expiredDate;
            do
            {
                if (packet.mailList == null || packet.mailList.Count <= 0)
                {
                    result = ePacketCommonResult.InvalidMailCount;
                    break;
                }

                // 리소스는 메일타입이 4까지만 존재하여 방어코드 추가
                var mailData = DataManager.Get<LoaderMailData>().GetMailData(mailType > (int)eMailType.GM ? (int)eMailType.GM : mailType);
                if (mailData == null)
                {
                    result = ePacketCommonResult.NotFoundMailData;
                    break;
                }

                // 메일의 등록 및 만료 날짜 계산. 반복 메일일 경우 받을 수 있는 유효 시간으로 따로 계산.
                if (mailType == (int)eMailType.RepeatGM)
                {
                    reservedDate = packet.startDate;
                    expiredDate = packet.endDate;
                }
                else
                {
                    reservedDate = now;
                    expiredDate = MailHelper.MakeExpireMailTime(now, mailData);
                }

                foreach (var systemMail in packet.mailList)
                {
                    eSendCurrencyType currencyType = eSendCurrencyType.None;
                    ePacketCommonResult checkSendMailCostResult = MailHelper.CheckSendMail(
                        userModel: null, 
                        mailData: mailData,
                        attachedItemSerial: 0,
                        attachedItemId: systemMail.attachedItemId,
                        attachedItemCount: systemMail.attachedItemCount,
                        cost: 0, 
                        currencyType: ref currencyType);

                    if (checkSendMailCostResult != ePacketCommonResult.Success)
                    {
                        result = checkSendMailCostResult;
                        break;
                    }
                    
                    var mailSendPacketData = new MailSendPacketData();
                    mailSendPacketData.MakeSendMailData(systemMail.title, systemMail.message,
                                                        systemMail.attachedItemId, systemMail.attachedItemCount,
                                                        reservedDate, expiredDate,
                                                        systemMail.receiveUid,
                                                        systemMail.receiveCharName, systemMail.fromSystemName, 0, 0, "", 0, mailType);

                    sendMailList.Add(mailSendPacketData);
                }

            } while (false);

            #region [로그] 81004 - 운영툴 서버 요청
            SLogManager.Instance.GameLog(81004, null
                , new GameLogParameter("serverGroupId", "" + ServerModule.Instance.GetMyServerGroupId())
                , new GameLogParameter("serverId", "" + ServerModule.Instance.GetMyServerId())
                , new GameLogParameter("requestType", "SystemMailSend")
                , new GameLogParameter("packet", "" + JsonConvert.SerializeObject(packet))
                , new GameLogParameter("result", "" + result.ToString())
                );

            #endregion

            if (result == ePacketCommonResult.Success)
            {
                var mailTask = PoolManager.Instance.GetObject<SystemMailThreadTask>();
                mailTask.SetMailSendTask(session, packet.requestKey, sendMailList);
                MailManager.Instance.AddTask(mailTask);
            }
            else
            {
                var failedPacket = new SC_Web_SystemMailSend();
                failedPacket.result = result;
                failedPacket.requestKey = packet.requestKey;
                session.SendPacket(PacketBuildWrapper.Build(failedPacket));
            }
        }

        [LayerMethodsAttribute(ePacketHandlerLayer.Community, typeof(CS_Web_ServerPush))]
        static void OnCS_Web_ServerPush(Session session, BasePacket reqPacket)
        {
            var packet = reqPacket as CS_Web_ServerPush;
            var result = ePacketCommonResult.Success;
            var now = K2Common.GetDateTime();

            do
            {
                SLogManager.Instance.InfoLog($"ServerPush, requestType: {packet.requestType}");

                switch (packet.requestType)
                {
                    // Gateway 로 전달
                    case eWebRequestType.GameConfigDataReload:
                    case eWebRequestType.ServerGroupDataReload:
                        {
                            var sendPacket = new CS_ZMQ_Web_ServerPush();
                            sendPacket.requestType = packet.requestType;
                            sendPacket.adminUserId = packet.adminUserId;
                            sendPacket.requestInfo.requestKey = packet.requestKey;

                            var gatewayServerInfo = ServerModule.Instance.Controller
                                .GetOptimalServerInfo(eServerType.Gateway, 0, 0);
                            if (gatewayServerInfo == null)
                            {
                                SLogManager.Instance.ErrorLog($"GetOptimalServerInfo failed, requestType: {packet.requestType}");
                                return;
                            }

                            sendPacket.broadcasterServerId = gatewayServerInfo.server_id;

                            foreach (var param in packet.paramList)
                            {
                                sendPacket.requestInfo.paramDic.Add(param.type, param);
                            }

                            ServerModule.Instance.Controller.BroadcastZMQ(eServerType.Gateway, 0, sendPacket);
                        }
                        break;
                    // Database 로 전달
                    case eWebRequestType.AdminMessageReload:
                        {
                            if (packet.paramList == null || packet.paramList.Count != 1)
                            {
                                result = ePacketCommonResult.InvalidWebParam;
                                break;
                            }

                            long requestIdx = packet.paramList[0].value;

                            var sendPacket = new CS_ZMQ_AdminMessageLoadQuery();
                            sendPacket.isInit = false;
                            sendPacket.requestIdx = requestIdx;

                            ServerModule.Instance.Controller.SendZMQ(eServerType.Database, sendPacket, eServerType.Community);
                        }
                        break;
                    case eWebRequestType.HivePgInfoSend:
                        {
                            if (packet.paramList == null || packet.paramList.Count != 1)
                            {
                                result = ePacketCommonResult.InvalidWebParam;
                                break;
                            }

                            #region [확인 후 제거] 데이터 검증
                            string hivePgInfo = packet.paramList.Find(x => x.type == eWebParamType.HivePgInfo)?.strValue;
                            if (hivePgInfo == null)
                            {
                                var packetParam = JsonConvert.SerializeObject(packet.paramList);
                                SLogManager.Instance.ExceptionLog($"HivePgInfo strValue Null, {packetParam}");
                                return;
                            }

                            JObject hivePgInfoObj = JsonConvert.DeserializeObject<JObject>(hivePgInfo);
                            if (hivePgInfoObj == null)
                            {
                                SLogManager.Instance.ExceptionLog($"HivePgInfo DeserializeObject failed, {hivePgInfo}");
                                return;
                            }

                            SLogManager.Instance.InfoLog($"OnCS_Web_ServerPush(). HivePgInfo(server_id : {hivePgInfoObj["server_id"]}, hiveiap_transaction_id : {hivePgInfoObj["hiveiap_transaction_id"]})");
                            #endregion

                            // 수신된 정보를 ZMQ 패킷으로 원하는 위치로 전송
                            var sendPacket = new CS_ZMQ_HivePgInfo();

                            sendPacket.result = ePacketCommonResult.Success;                                         //성공으로 처리                        
                            sendPacket.hiveReceipt = hivePgInfoObj["hiveiap_receipt"].ToString();
                            sendPacket.hiveTransactionId = hivePgInfoObj["hiveiap_transaction_id"].ToString();
                            sendPacket.hiveMarketPId = hivePgInfoObj["market_pid"].ToString();
                            sendPacket.hiveiapMarketId = Convert.ToInt32(hivePgInfoObj["market_id"]);
                            sendPacket.playerId = Convert.ToInt64(hivePgInfoObj["uid"]);
                            // 해당 게임 서버로 패킷 전송 해야 함. _blackBoardUIdDic 추가

                            var userBBModel = CommunityWorkerManager.Instance.GetUserBlackBoardByUid(sendPacket.playerId);

                            if (userBBModel == null || userBBModel.userInfoData == null)
                            {
                                SLogManager.Instance.GameLog(14004, null, new GameLogParameter("failReason", "유저 정보를 찾을 수 없음")
                                                                        , new GameLogParameter("hiveIapMarketPID", "" + sendPacket.hiveMarketPId)
                                                                        , new GameLogParameter("hiveIapMarketID", "" + sendPacket.hiveiapMarketId)
                                                                        , new GameLogParameter("hiveTransactionID", "" + sendPacket.hiveTransactionId));
                                break;
                            }

                            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, userBBModel.userInfoData.serverId);
                        }
                        break;
                    case eWebRequestType.None:
                    default:
                        break;
                }
            }
            while (false);

            #region [로그] 81004 - 운영툴 서버 요청
            SLogManager.Instance.GameLog(81004, null
                , new GameLogParameter("serverGroupId", "" + ServerModule.Instance.GetMyServerGroupId())
                , new GameLogParameter("serverId", "" + ServerModule.Instance.GetMyServerId())
                , new GameLogParameter("requestType", "" + packet.requestType)
                , new GameLogParameter("packet", "" + JsonConvert.SerializeObject(packet))
                , new GameLogParameter("result", "" + result.ToString())
                );

            #endregion

            if (result != ePacketCommonResult.Success)
            {
                var failedPacket = new SC_Web_ServerPush();
                failedPacket.result = result;
                failedPacket.requestKey = packet.requestKey;
                failedPacket.requestType = packet.requestType;
                session.SendPacket(PacketBuildWrapper.Build(failedPacket));
            }
        }

        /// <summary>
        /// 컨텐츠 온오프 요청 전달
        /// </summary>
        /// <param name="session"></param>
        /// <param name="reqPacket"></param>
        [LayerMethodsAttribute(ePacketHandlerLayer.Community, typeof(CS_Web_BlockContents))]
        static void OnCS_Web_BlockContents(Session session, BasePacket reqPacket)
        {
            var packet = reqPacket as CS_Web_BlockContents;
            var result = ePacketCommonResult.Success;

            do
            {
                if (packet.blockContents == null || packet.blockContents.Count <= 0)
                {
                    result = ePacketCommonResult.InvalidBlockContents;
                    break;
                }

                int gameGroupId = ServerModule.Instance.GetMyServerGroupId();
                if (gameGroupId != packet.serverGroupId)
                {
                    result = ePacketCommonResult.InvalidSelectServerGroupId;
                    break;
                }

                BlockContentsCommunityManager.Instance.UpdateBlockContents(packet.blockContents);
            }
            while (false);

            #region [로그] 81004 - 운영툴 서버 요청
            SLogManager.Instance.GameLog(81004, null
                , new GameLogParameter("serverGroupId", "" + ServerModule.Instance.GetMyServerGroupId())
                , new GameLogParameter("serverId", "" + ServerModule.Instance.GetMyServerId())
                , new GameLogParameter("requestType", "BlockContents")
                , new GameLogParameter("packet", "" + JsonConvert.SerializeObject(packet))
                , new GameLogParameter("result", "" + result.ToString())
                );
            #endregion

            if (result != ePacketCommonResult.Success)
            {
                var failedPacket = new SC_Web_BlockContents();
                failedPacket.result = result;
                failedPacket.requestKey = packet.requestKey;
                failedPacket.serverGroupId = packet.serverGroupId;
                session.SendPacket(PacketBuildWrapper.Build(failedPacket));
            }
        }

        /// <summary>
        /// 공용 이벤트 형식 요청
        /// </summary>
        /// <param name="session"></param>
        /// <param name="reqPacket"></param>
        [LayerMethodsAttribute(ePacketHandlerLayer.Community, typeof(CS_Web_EventAction))]
        static void OnCS_Web_EventAction(Session session, BasePacket reqPacket)
        {
            var packet = reqPacket as CS_Web_EventAction;
            var result = ePacketCommonResult.Success;
            EventEntity eventData = null;

            do
            {
                try
                {
                    if (packet.eventIdx > 0)
                    {
                        JObject jsonValidateObj = JsonConvert.DeserializeObject<JObject>(packet.eventData);
                        if (jsonValidateObj.GetValue("author") != null)
                        {
                            jsonValidateObj.Remove("author");
                        }

                        eventData = JsonConvert.DeserializeObject<EventEntity>(JsonConvert.SerializeObject(jsonValidateObj));
                    }

                    switch (packet.webActionType)
                    {
                        case eWebActionType.EventAdd:
                            if (eventData != null)
                            {
                                if (packet.eventIdx != eventData.idx)
                                {
                                    result = ePacketCommonResult.InvalidEventType;
                                    break;
                                }

                                EventCommunityManager.Instance.EventAdd(eventData);
                            }
                            break;
                        case eWebActionType.EventUpdate:
                            if (eventData != null)
                            {
                                if (packet.eventIdx != eventData.idx)
                                {
                                    result = ePacketCommonResult.InvalidEventType;
                                    break;
                                }

                                EventCommunityManager.Instance.EventUpdate(eventData);
                            }
                            break;
                        case eWebActionType.EventDelete:
                            if (packet.eventIdx > 0)
                            {
                                EventCommunityManager.Instance.EventDelete(packet.eventIdx);
                            }
                            else
                            {
                                result = ePacketCommonResult.InvalidEventType;
                            }
                            break;
                        case eWebActionType.EventMissionRemove:
                            result = EventCommunityManager.Instance.EventMissionRemove(packet.eventMissionId, (eEventType)packet.eventMissionType, packet.serverGroupId);
                            break;
                        case eWebActionType.EventMissionUpdate:
                            result = EventCommunityManager.Instance.EventMissionUpdate(packet.eventMissionId, (eEventType)packet.eventMissionType, packet.serverGroupId);
                            break;
                    }

                    break;
                }
                catch (Exception e)
                {
                    SLogManager.Instance.ExceptionLog($"Failed EventAction => {e.ToString()}");

                    result = ePacketCommonResult.FailedEventMissionUpdate;
                    break;
                }

            } while (false);

            #region [로그] 81004 - 운영툴 서버 요청
            SLogManager.Instance.GameLog(81004, null
                , new GameLogParameter("serverGroupId", "" + ServerModule.Instance.GetMyServerGroupId())
                , new GameLogParameter("serverId", "" + ServerModule.Instance.GetMyServerId())
                , new GameLogParameter("requestType", "EventAction")
                , new GameLogParameter("packet", "" + JsonConvert.SerializeObject(packet))
                , new GameLogParameter("result", "" + result.ToString())
                );

            #endregion

            if (result != ePacketCommonResult.Success)
            {
                var failedPacket = new SC_Web_EventAction();
                failedPacket.result = result;
                failedPacket.requestKey = packet.requestKey;
                failedPacket.serverGroupId = packet.serverGroupId;
                session.SendPacket(PacketBuildWrapper.Build(failedPacket));
            }

        }

        /// <summary>
        /// Game User 요청 전달
        /// </summary>
        /// <param name="session"></param>
        /// <param name="reqPacket"></param>
        [LayerMethodsAttribute(ePacketHandlerLayer.Community, typeof(CS_WebAction))]
        static void OnCS_WebAction(Session session, BasePacket reqPacket)
        {
            var packet = reqPacket as CS_WebAction;
            var result = ePacketCommonResult.Success;
            var now = K2Common.GetDateTime();

            do
            {
                switch (packet.type)
                {
                    case eWebActionType.None:
                        // 미정의 또는 필수 파라미터 없는 경우
                        //result = ePacketCommonResult.None;
                        break;
                    case eWebActionType.ExpChange:
                    case eWebActionType.LossItemRecovery:
                    case eWebActionType.JumpQuest:
                    case eWebActionType.ItemPkLimitDateUpdate:
                    case eWebActionType.ItemDelete:
                    case eWebActionType.ItemUnDelete:
                    case eWebActionType.ItemCountUpdate:
                    case eWebActionType.AddCurrency:
                    case eWebActionType.SubCurrency:
                    case eWebActionType.TutorialSkip:

                        if (packet.paramList == null || packet.paramList.Count <= 0)
                        {
                            result = ePacketCommonResult.InvalidWebParam;
                            break;
                        }

                        SLogManager.Instance.InfoLog("OnCS_WebAction(). RequestKey : {0}, targetUid : {1}, targetCharacterId : {2}, AdminUserId : {3}"
                            , packet.requestKey, packet.targetUid, packet.targetCharacterId, packet.adminUserId);

                        var sendPacket = new CS_ZMQ_WebAction();
                        sendPacket.type = packet.type;
                        sendPacket.requestInfo.serverGroupId = packet.serverGroupId;
                        sendPacket.requestInfo.targetUid = packet.targetUid;
                        sendPacket.requestInfo.targetCharacterId = packet.targetCharacterId;
                        sendPacket.requestInfo.requestKey = packet.requestKey;

                        foreach (var param in packet.paramList)
                        {
                            sendPacket.requestInfo.paramDic.Add(param.type, param);
                        }

                        ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community);

                        break;
                    default:
                        SLogManager.Instance.InfoLog("OnCS_WebAction(). RequestKey : {0}, targetUid : {1}, targetCharacterId : {2}, AdminUserId : {3}, WebActionType : {4}"
                            , packet.requestKey, packet.targetUid, packet.targetCharacterId, packet.adminUserId, packet.type);
                        break;
                }

                break;
            }
            while (false);

            #region [로그] 81002 - 운영툴 요청
            SLogManager.Instance.GameLog(81002, null
                , new GameLogParameter("uId", "" + packet.targetUid)
                , new GameLogParameter("charId", "" + packet.targetCharacterId)
                , new GameLogParameter("serverGroupId", "" + packet.serverGroupId)
                , new GameLogParameter("serverId", "" + ServerModule.Instance.GetMyServerId())
                , new GameLogParameter("requestType", "" + packet.type)
                , new GameLogParameter("packet", "" + JsonConvert.SerializeObject(packet))
                , new GameLogParameter("result", "" + result.ToString())
                );

            #endregion

            if (result != ePacketCommonResult.Success)
            {
                var failedPacket = new SC_WebAction();
                failedPacket.result = result;
                failedPacket.requestKey = packet.requestKey;
                session.SendPacket(PacketBuildWrapper.Build(failedPacket));
            }
        }

        /// <summary>
        /// Guild 관련 요청 전달
        /// </summary>
        /// <param name="session"></param>
        /// <param name="reqPacket"></param>
        [LayerMethodsAttribute(ePacketHandlerLayer.Community, typeof(CS_WebGuildAction))]
        static void OnCS_WebGuildAction(Session session, BasePacket reqPacket)
        {
            var packet = reqPacket as CS_WebGuildAction;
            var result = ePacketCommonResult.Success;
            var now = K2Common.GetDateTime();

            do
            {
                switch (packet.type)
                {
                    case eGuildActionType.None:
                        // 미정의 또는 필수 파라미터 없는 경우
                        //result = ePacketCommonResult.None;
                        break;
                    case eGuildActionType.SettingChange:
                    case eGuildActionType.NoticeChange:
                    case eGuildActionType.MemberGradeChange:
                    case eGuildActionType.MemberIntroduceChange:

                        if (packet.paramList == null || packet.paramList.Count <= 0)
                        {
                            result = ePacketCommonResult.InvalidWebParam;
                            break;
                        }

                        SLogManager.Instance.InfoLog("OnCS_WebGuildAction(). RequestKey : {0}, targetUid : {1}, targetCharacterId : {2}, AdminUserId : {3}"
                            , packet.requestKey, packet.targetUid, packet.targetCharacterId, packet.adminUserId);

                        //public void SendZMQGuildAction(eGuildActionType type, List<GuildParam> paramList)
                        {
                            var guildActionPacket = new CS_ZMQ_GuildAction();

                            guildActionPacket.type = packet.type;
                            guildActionPacket.requesterInfo.charId = packet.targetCharacterId;
                            guildActionPacket.requesterInfo.charName = packet.targetCharacterName;
                            guildActionPacket.requesterInfo.charTableId = packet.targetCharacterTableId;
                            //guildActionPacket.requesterInfo.sourceServerId = ServerModule.Instance.GetMyServerId();

                            foreach (var param in packet.paramList)
                            {
                                guildActionPacket.requesterInfo.paramDic.Add(param.type, param);
                            }

                            //ServerModule.Instance.GetServerController().SendZMQ(eServerType.Game, guildActionPacket, eServerType.Community);

                            var cmd = new GuildActionReqCmd();
                            cmd.Init(packet.targetUid, ServerModule.Instance.GetMyServerId(), packet.serverGroupId, "");
                            cmd.type = packet.type;
                            cmd.requesterInfo.Copy(guildActionPacket.requesterInfo);

                            GuildCommunityManager.Instance.AddCommand(cmd);
                        }

                        break;
                    default:
                        SLogManager.Instance.InfoLog("OnCS_WebAction(). RequestKey : {0}, targetUid : {1}, targetCharacterId : {2}, AdminUserId : {3}, WebActionType : {4}"
                            , packet.requestKey, packet.targetUid, packet.targetCharacterId, packet.adminUserId, packet.type);
                        break;
                }

                break;
            }
            while (false);

            #region [로그] 81002 - 운영툴 요청
            SLogManager.Instance.GameLog(81002, null
                , new GameLogParameter("uId", "" + packet.targetUid)
                , new GameLogParameter("charId", "" + packet.targetCharacterId)
                , new GameLogParameter("serverGroupId", "" + packet.serverGroupId)
                , new GameLogParameter("serverId", "" + ServerModule.Instance.GetMyServerId())
                , new GameLogParameter("requestType", "" + packet.type)
                , new GameLogParameter("packet", "" + JsonConvert.SerializeObject(packet))
                , new GameLogParameter("result", "" + result.ToString())
                );

            #endregion

            if (result != ePacketCommonResult.Success)
            {
                var failedPacket = new SC_WebGuildAction();
                failedPacket.result = result;
                failedPacket.requestKey = packet.requestKey;
                session.SendPacket(PacketBuildWrapper.Build(failedPacket));
            }
        }
    }
}
