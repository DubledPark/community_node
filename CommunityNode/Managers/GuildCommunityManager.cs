using K2Packet.DataStruct;
using K2Server.Database.Entities;
using K2Server.Managers;
using K2Server.Models;
using K2Server.Packet;
using K2Server.Packet.Protocol;
using K2Server.ServerNodes.CommunityNode.Commands;
using K2Server.ServerNodes.CommunityNode.Models;
using K2Server.ServerNodes.CommunityNode.Thread;
using K2Server.Util;
using Newtonsoft.Json;
using RRCommon.Enum;
using SharedLib;
using SharedLib.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;


namespace K2Server.ServerNodes.CommunityNode.Managers
{
    class GuildCommunityManager : Singleton<GuildCommunityManager>
    {
        ThreadModel_GuildCommunityWorker _workThread = new ThreadModel_GuildCommunityWorker();

        // <guildId, GuildCommunityModel>
        ConcurrentDictionary<long, GuildCommunityModel> _guildDic = new ConcurrentDictionary<long, GuildCommunityModel>();
        // <guildName, GuildCommunityModel>
        ConcurrentDictionary<string, GuildCommunityModel> _guildByNameDic = new ConcurrentDictionary<string, GuildCommunityModel>();

        // <charId, guildId>    
        // 해당 케릭터의 길드 정보 조회 여부 판단
        // 없으면 케릭터에 대한 정보 조회를 아직 하지 않은 상태,  
        // 있는데 0 이면 과거에 길드를 가입하고 탈퇴한 경우.
        Dictionary<long, long> _guildByCharIdDic = new Dictionary<long, long>();

        // <charId, <guildId, GuildRecruitEntity>> (가입요청) // GuildId:0 으로 추가되어 있을시 재가입 패널티중
        ConcurrentDictionary<long, ConcurrentDictionary<long, GuildRecruitEntity>> _guildJoinByCharIdDic
            = new ConcurrentDictionary<long, ConcurrentDictionary<long, GuildRecruitEntity>>();
        // <guildId, <charId, GuildRecruitEntit>> (가입요청)
        ConcurrentDictionary<long, ConcurrentDictionary<long, GuildRecruitEntity>> _guildJoinByGuildIdDic
            = new ConcurrentDictionary<long, ConcurrentDictionary<long, GuildRecruitEntity>>();

        // <srcGuildId, targetGuildId>
        // 동맹, 적대 관계의 길드가 제대로 읽혔는지 체크
        // 읽히지 않은 길드에 대한 별도의 처리를 위해 사용
        DelayedEventModel<long, long> _delayedRelationLoadEvent = new DelayedEventModel<long, long>();
        SortedSet<long> _notCompleteLoadGuildIdSet = new SortedSet<long>();

        int USER_JOIN_REQUEST_MAX_COUNT;
        int GUILD_JOIN_REQUEST_MAX_COUNT;
        int _myServerGroupId;

        public void Init()
        {
            var data = DataManager.Get<LoaderGuildOption>().GetGuildOptionData(eOptionType.MaxApply);
            if (data != null)
                USER_JOIN_REQUEST_MAX_COUNT = data.OptionValue;
            else
                USER_JOIN_REQUEST_MAX_COUNT = 5;

            data = DataManager.Get<LoaderGuildOption>().GetGuildOptionData(eOptionType.MemberApplyMax);
            if (data != null)
                GUILD_JOIN_REQUEST_MAX_COUNT = data.OptionValue;
            else
                GUILD_JOIN_REQUEST_MAX_COUNT = 50;

            _delayedRelationLoadEvent.Init(K2Const.DELAYED_EVENT_EXPIRE_SEC, OnCompleteLoadGuild);

            _myServerGroupId = ServerModule.Instance.GetMyServerGroupId();

            var serverController = ServerModule.Instance.Controller;
            if (false == serverController.SendZMQ(eServerType.Database, new CS_ZMQ_AllGuildInfoQuery(), eServerType.Community))
            {
                SLogManager.Instance.ErrorLog("Send CS_ZMQ_AllGuildInfoQuery() Error.");
            }

            CmdGuildCommunityHandler.CmdBind();
        }

        public void AddCommand(BaseGuildCommunityCmd cmd)
        {
            _workThread.AddTaskQueue(cmd);
        }

        public void InitReqAllGuildInfoList(List<GuildInfoData> guildInfoList, List<GuildRecruitEntity> guildRecruitList)
        {
            if (guildInfoList != null && guildInfoList.Count > 0)
            {
                foreach (var guildInfo in guildInfoList)
                {
                    var guildModel = PoolManager.Instance.GetObject<GuildCommunityModel>();
                    guildModel.Init(guildInfo, false);

                    _guildDic.TryAdd(guildModel.guildId, guildModel);
                    _guildByNameDic.TryAdd(guildModel.guildName, guildModel);

                    foreach (var guildMember in guildInfo.memberList)
                    {
                        if (_guildByCharIdDic.ContainsKey(guildMember.charId) == false)
                        {
                            _guildByCharIdDic.Add(guildMember.charId, guildMember.guildId);
                        }
                    }
                }
            }

            if (guildRecruitList != null && guildRecruitList.Count > 0)
            {
                foreach (var guildRecruit in guildRecruitList)
                {
                    var guildRecruitEntity = PoolManager.Instance.GetObject<GuildRecruitEntity>();
                    guildRecruitEntity.Copy(guildRecruit);

                    if (_guildJoinByCharIdDic.ContainsKey(guildRecruitEntity.charId) == false)
                    {
                        _guildJoinByCharIdDic.TryAdd(guildRecruitEntity.charId, new ConcurrentDictionary<long, GuildRecruitEntity>());
                    }

                    _guildJoinByCharIdDic[guildRecruitEntity.charId].TryAdd(guildRecruitEntity.guildId, guildRecruitEntity);

                    if (_guildJoinByGuildIdDic.ContainsKey(guildRecruitEntity.guildId) == false)
                    {
                        _guildJoinByGuildIdDic.TryAdd(guildRecruitEntity.guildId, new ConcurrentDictionary<long, GuildRecruitEntity>());
                    }

                    _guildJoinByGuildIdDic[guildRecruitEntity.guildId].TryAdd(guildRecruitEntity.charId, guildRecruitEntity);
                }
            }

            _workThread.Start(0);
        }

        public void ReqGuildCheck(long charId, long uId, bool needDBReq = true)
        {
            if (_guildByCharIdDic.TryGetValue(charId, out long guildId) == true)
            {
                var packet = new SC_ZMQ_GuildCheck();
                packet.charId = charId;
                packet.uId = uId;

                if (guildId != 0)
                {
                    if (_guildDic.TryGetValue(guildId, out GuildCommunityModel guild) == true)
                    {
                        packet.guildInfo = guild.GetGuildInfoData();
                    }
                    else
                    {
                        // 나오면 안되는 오류.....
                        SLogManager.Instance.ErrorLog($"Not exist guildModel - charId:{charId}, guildId:{guildId}");
                    }
                }

                //var myServerGroupId = ServerModule.Instance.GetMyServerGroupId();
                //ServerModule.Instance.GetServerController().BroadcastZMQ(eServerType.Game, myServerGroupId, packet);
                BroadcastZMQToGameNode(packet);
            }
            else if (needDBReq == true)
            {
                SendZMQGuildInfoQuery(eGuildLoadType.CharacterLogin, charId, 0, null);
            }
            else
            {
                // 나오면 안되는 오류.....
                SLogManager.Instance.ErrorLog($"Not exist _guildByCharIdDic - charId:{charId}, uId:{uId}");
            }
        }

        public void ReqGuildCheck(long guildId)
        {
            // 동맹, 연맹도 game서버에 길드 정보 전송
            if (_guildDic.TryGetValue(guildId, out GuildCommunityModel guild) == true)
            {
                var packet = new SC_ZMQ_GuildCheck();
                packet.charId = 0;
                packet.uId = 0;

                packet.guildInfo.guild.Copy(guild.GetGuildInfo());
                foreach (var member in guild.GetGuildMemberList())
                {
                    packet.guildInfo.memberList.Add(member.Clone());
                }

                //var myServerGroupId = ServerModule.Instance.GetMyServerGroupId();
                //ServerModule.Instance.GetServerController().BroadcastZMQ(eServerType.Game, myServerGroupId, packet);
                BroadcastZMQToGameNode(packet);
            }
            else
            {
                SLogManager.Instance.ErrorLog($"Not exist guildModel - guildId:{guildId}");
            }
        }

        public void ReqGuildInfo(List<long> guildIdList, int serverId)
        {
            var sendPacket = new SC_ZMQ_GuildInfoSync();

            foreach (var guildId in guildIdList)
            {
                _guildDic.TryGetValue(guildId, out GuildCommunityModel guild);
                if (guild == null)
                    continue;

                sendPacket.guildInfoList.Add(guild.GetGuildInfoData());
            }

            if (sendPacket.guildInfoList.Count > 0)
            {
                ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, serverId);
            }
        }

        public void ReqGuildCreate(GuildRequesterData masterInfo)
        {
            //var coolTime = DataManager.Get<LoaderGuildOption>().GetGuildOptionData(eOptionType.LeavePenalty);

            if (masterInfo.paramDic.Count <= 3)
            {
                SLogManager.Instance.ErrorLog("ReqGuildCreate()",
                    "invalid param - requesterCharId:{0}, requesterName:{1}",
                    masterInfo.charId, masterInfo.charName);
                SendGuildCreate(ePacketCommonResult.InvalidGuildParam, masterInfo, null);
                return;
            }

            if (_guildByCharIdDic.ContainsKey(masterInfo.charId) == true &&
                    _guildByCharIdDic[masterInfo.charId] > 0)
            {
                SendGuildCreate(ePacketCommonResult.AlreadyGuildJoined, masterInfo, null);
                return;
            }

            if (CheckGuildRejoinCoolTime(masterInfo.charId, out var _) == true)
            {
                SendGuildCreate(ePacketCommonResult.CannotGuildRejoinCoolTime, masterInfo, null);
                return;
            }

            masterInfo.GetParamValue(eGuildParamType.CrestId, out long crestId);
            masterInfo.GetParamValue(eGuildParamType.JoinType, out long joinType);
            masterInfo.GetParamValue(eGuildParamType.Password, out long password);
            masterInfo.GetParamValue(eGuildParamType.GuildName, out string guildName);

            var memberEntity = new GuildMemberEntity();
            var guildEntity = new GuildEntity();

            guildEntity.Init(masterInfo.charId, masterInfo.charName, masterInfo.charTableId, guildName, (int)crestId, (eGuildJoinType)joinType, (int)password);
            memberEntity.Init(masterInfo.charId, masterInfo.charName, masterInfo.charTableId, guildEntity.id, 1, false);

            SendZMQGuildInfoSyncSave(eGuildInfoSyncSaveType.Create, guildEntity.id, masterInfo, guildEntity, memberEntity, null, null);
        }

        public void ReqGuildJoin(GuildRequesterData requesterInfo, bool needDBSearch = true)
        {
            var result = ePacketCommonResult.Success;
            GuildCommunityModel guildModel = null;

            do
            {
                if (requesterInfo.paramDic.Count <= 2)
                {
                    SLogManager.Instance.ErrorLog("ReqGuildJoin()", "invalid param - requesterCharId:{0}, requesterName:{1}", requesterInfo.charId, requesterInfo.charName);
                    result = ePacketCommonResult.InvalidGuildParam;
                    break;
                }

                requesterInfo.GetParamValue(eGuildParamType.GuildId, out long guildId);
                requesterInfo.GetParamValue(eGuildParamType.Password, out long password);
                requesterInfo.GetParamValue(eGuildParamType.JoinType, out long joinTypeValue);
                requesterInfo.GetParamValue(eGuildParamType.Introduce, out string introduce);

                if (_guildDic.TryGetValue(guildId, out guildModel) == false)
                {
                    result = ePacketCommonResult.NotFoundGuild;
                    break;
                }

                var guildEntity = guildModel.GetGuildInfo();
                if (guildEntity?.state == (byte)eGuildState.Dissolved)
                {
                    result = ePacketCommonResult.InvalidGuildJoinRequest;
                    break;
                }

                var joinType = (eGuildJoinType)joinTypeValue;
                if (joinType == eGuildJoinType.Cancel)
                {
                    if (_guildJoinByCharIdDic.TryGetValue(requesterInfo.charId, out var recruitDic) == false)
                    {
                        SLogManager.Instance.ErrorLog($"not fount guild join request1 - charId:{requesterInfo.charId}, guildId:{guildId}");
                        result = ePacketCommonResult.NotFoundGuildJoinRequest;
                        break;
                    }
                    
                    if (recruitDic.TryGetValue(guildId, out var recruitEntity) == false)
                    {
                        SLogManager.Instance.ErrorLog($"not fount guild join request2 - charId:{requesterInfo.charId}, guildId:{guildId}");
                        result = ePacketCommonResult.NotFoundGuildJoinRequest;
                        break;
                    }

                    var clone = recruitEntity.Clone();
                    clone.Delete();

                    SendZMQGuildInfoSyncSave(eGuildInfoSyncSaveType.JoinCancel, guildId, requesterInfo, null, null, clone, null);
                    break;
                }

                result = IsEnableGuildJoin(joinType, requesterInfo.charId, guildModel, (int)password, true);
                if (result != ePacketCommonResult.Success)
                {
                    break;
                }

                if (joinType == eGuildJoinType.AutoJoin || joinType == eGuildJoinType.Password)
                {
                    var memberEntity = new GuildMemberEntity();
                    memberEntity.Init(requesterInfo.charId, requesterInfo.charName, requesterInfo.charTableId, guildModel.guildId, 5);
                    SendZMQGuildInfoSyncSave(eGuildInfoSyncSaveType.JoinDirect, guildId, requesterInfo,
                        guildModel.GetGuildInfo(), memberEntity, null, guildModel.GetGuildRelationList());
                    break;
                }
                
                if (joinType == eGuildJoinType.NeedAccept)
                {
                    var recruitEntity = new GuildRecruitEntity();
                    recruitEntity.Init(requesterInfo.charId, requesterInfo.charName, requesterInfo.charTableId, guildModel.guildId, introduce);
                    SendZMQGuildInfoSyncSave(eGuildInfoSyncSaveType.JoinRequest, guildId, requesterInfo, null, null, recruitEntity, null);
                    break;
                }
            }
            while (false);

            if (result != ePacketCommonResult.Success)
                SendZMQGuildJoinResult(result, requesterInfo, null);
        }

        private ePacketCommonResult IsEnableGuildJoin(eGuildJoinType joinType, long targetCharId, GuildCommunityModel guildModel, int password = 0, bool isJoinRequest = false)
        {
            var result = ePacketCommonResult.Success;

            if (_guildByCharIdDic.ContainsKey(targetCharId) == true &&
                _guildByCharIdDic[targetCharId] > 0)
            {
                result = ePacketCommonResult.AlreadyGuildJoined;
            }

            if (guildModel.IsEnermyToGuild(targetCharId))
            {
                return ePacketCommonResult.CannotJoinGuildForEnermy;
            }
            
            if (isJoinRequest == true)
            {
                if (joinType == eGuildJoinType.None)
                {
                    return ePacketCommonResult.InvalidGuildParam;
                }

                if (guildModel.joinType != joinType)
                {
                    return ePacketCommonResult.InvalidGuildJoinRequest;
                }
                
                if (guildModel.joinType == eGuildJoinType.Disable)
                {
                    return ePacketCommonResult.DisableGuildJoin;
                }
                
                if (CheckGuildRejoinCoolTime(targetCharId, out var penaltyRecruit) == true)
                {
                    return ePacketCommonResult.CannotGuildRejoinCoolTime;
                }

                if (joinType == eGuildJoinType.NeedAccept)
                {
                    var now = K2Common.GetDateTime();
                    if (_guildJoinByCharIdDic.ContainsKey(targetCharId) == true)
                    {
                        if (_guildJoinByCharIdDic[targetCharId].ContainsKey(guildModel.guildId) == true)
                        {
                            return ePacketCommonResult.AlreadyGuildJoinRequest;
                        }
                        
                        if (_guildJoinByCharIdDic[targetCharId].Values.Where(e => e.IsValidPeriod(now)).Count() >= USER_JOIN_REQUEST_MAX_COUNT)
                        {
                            return ePacketCommonResult.OverCountGuildJoinRequest;
                        }
                    }

                    if (_guildJoinByGuildIdDic.ContainsKey(guildModel.guildId) == true)
                    {
                        if (_guildJoinByGuildIdDic[guildModel.guildId].Values.Where(e => e.IsValidPeriod(now)).Count() >= GUILD_JOIN_REQUEST_MAX_COUNT)
                        {
                            return ePacketCommonResult.OverCountGuildJoinBeRequested;
                        }
                    }
                }
                
                if (joinType == eGuildJoinType.Password &&
                    guildModel.password != password)
                {
                    return ePacketCommonResult.WrongGuildJoinPassword;
                }
            }

            if (guildModel.CheckReservedJoin(targetCharId) == false)
            {
                return ePacketCommonResult.GuildMemberFull;
            }

            return result;
        }

        private bool CheckGuildRejoinCoolTime(long charId, out GuildRecruitEntity penaltyRecruit)
        {
            // 임시로 쿨타임 해제
            //penaltyRecruit = _guildJoinByCharIdDic[charId][0];
            //return false;

            penaltyRecruit = null;
            if (_guildJoinByCharIdDic.ContainsKey(charId) == true &&
               _guildJoinByCharIdDic[charId].ContainsKey(0) == true)
            {
                penaltyRecruit = _guildJoinByCharIdDic[charId][0];
            }

            var coolTime = DataManager.Get<LoaderGuildOption>().GetGuildOptionData(eOptionType.LeavePenalty);
            if (coolTime != null &&
                penaltyRecruit != null &&
                penaltyRecruit.regDate.AddHours(coolTime.OptionValue) > K2Common.GetDateTime())
            {
                return true;
            }

            return false;
        }

        public GuildCommunityModel GetGuildModel(long charId)
        {
            GuildCommunityModel guildModel = null;
            if (_guildByCharIdDic.ContainsKey(charId) == false ||
                _guildByCharIdDic[charId] == 0 ||
                _guildDic.TryGetValue(_guildByCharIdDic[charId], out guildModel) == false)
            {
                return null;
            }

            return guildModel;
        }

        public GuildCommunityModel GetGuildModelByGuildId(long guildId)
        {
            _guildDic.TryGetValue(guildId, out var guildModel);
            return guildModel;
        }

        /// <summary>
        /// 길드 정보 조회 by GuildId
        /// </summary>
        /// <param name="guildId"></param>
        /// <param name="onlineCharId"></param>
        /// <returns></returns>
        private GuildCommunityModel GetGuildModelByGuildId(long guildId, out long onlineCharId)
        {
            onlineCharId = 0;

            GuildCommunityModel guildModel = GetGuildModelByGuildId(guildId);
            if (guildModel == null)
            {
                return null;
            }

            // 길드원의 접속 정보 갱신
            //foreach (var guildMemberData in guildModel.GetGuildMemberList())
            //{
            //    CheckCharacterLogin(guildMemberData.charId);
            //}

            // 길드 마스터가 온라인인지 확인
            var guildMemberEntity = guildModel.GetGuildMemberList().Where(p => p.grade == (int)eGuildMemberGrade.Master).FirstOrDefault();

            if (guildMemberEntity == null)
            {
                // 부재 시 다른 온라인 유저 정보 조회
                guildMemberEntity = guildModel.GetGuildMemberList().Where(p => p.isConnected == true).FirstOrDefault();
            }

            if (guildMemberEntity != null)
            {
                onlineCharId = guildMemberEntity.charId;
            }

            return guildModel;
        }

        public void ReqGuildRejoinCoolTimeAction(GuildRequesterData requesterInfo, eGuildRejoinCoolTimeActionType actionType)
        {
            var isExistCoolTime = CheckGuildRejoinCoolTime(requesterInfo.charId, out var penaltyRecruit);

            if (isExistCoolTime == true)
            {
                if (actionType == eGuildRejoinCoolTimeActionType.Remove)
                {
                    var clone = penaltyRecruit.Clone();
                    clone.Delete();

                    SendZMQGuildInfoSyncSave(eGuildInfoSyncSaveType.RejoinCoolTimeRemove, 0, requesterInfo, null, null, clone, null);
                }
                else
                {
                    SendZMQGuildRejoinCoolTimeActionResult(ePacketCommonResult.CannotGuildRejoinCoolTime, requesterInfo, actionType);
                }
            }
            else
            {
                SendZMQGuildRejoinCoolTimeActionResult(ePacketCommonResult.NoExistGuildRejoinCoolTime, requesterInfo, actionType);
            }
        }

        public ePacketCommonResult ReqGuildAction(eGuildActionType type, GuildRequesterData requesterInfo, bool needDBReq = true)
        {
            var result = ePacketCommonResult.Success;
            var guildModel = GetGuildModel(requesterInfo.charId);
            if (guildModel != null && guildModel.GetGuildState() != eGuildState.Dissolved)
            {
                switch (type)
                {
                    case eGuildActionType.JoinAccept:
                        result = ReqGuildJoinAccept(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.JoinReject:
                        result = ReqGuildJoinReject(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.Banish:
                        result = ReqGuildBanish(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.Leave:
                        result = ReqGuildLeave(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.Dissolve:
                        result = ReqGuildDissolve(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.Give:
                        result = ReqGuildGive(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.AddCrest:
                        result = ReqAddCrest(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.MemberIntroduceChange:
                        result = ReqGuildMemberIntroduceChange(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.MemberQuestInit:
                        result = ReqGuildMemberQuestInfoChange(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.CrestChange:
                        result = ReqGuildCrestChange(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.NoticeChange:
                        result = ReqGuildNoticeChange(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.MemberGradeChange:
                        result = ReqGuildMemberGradeChange(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.SettingChange:
                        result = ReqGuildSettingChange(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.ReqAlliance:
                        result = ReqGuildAlliance(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.AcceptAlliance:
                        result = ReqGuildAllianceAccept(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.RejectAlliance:
                        result = ReqGuildAllianceReject(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.RemoveAlliance:
                        result = ReqGuildAllianceRemove(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.AddHostile:
                        result = ReqGuildAddHostile(requesterInfo, guildModel, needDBReq);
                        break;
                    case eGuildActionType.RemoveHostile:
                        result = ReqGuildRemoveHostile(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.AddEnermy:
                        result = ReqGuildAddEnermy(requesterInfo, guildModel, null);
                        break;
                    case eGuildActionType.RemoveEnermy:
                        result = ReqGuildRemoveEnemy(requesterInfo, guildModel, null);
                        break;
                    case eGuildActionType.AddGuildSkill:
                        result = ReqGuildSkillAdd(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.EnchantGuildSkill:
                        result = ReqGuildSkillEnchant(requesterInfo, guildModel);
                        break;
                }
            }
            else
            {
                result = ePacketCommonResult.NotGuildJoined;
                SendGuildActionResult(result, type, requesterInfo, null);
            }

            return result;
        }

        public ePacketCommonResult ReqGuildActionByGmTool(eGuildActionType type, GuildRequesterData requesterInfo, bool needDBReq = true)
        {
            var result = ePacketCommonResult.Success;

            if (requesterInfo.paramDic.TryGetValue(eGuildParamType.GmToolAction, out var isGmToolAction) == false)
            {
                result = ePacketCommonResult.InternalError;
            }

            GuildParam guildParam;
            long guildId = 0;
            long onlineCharId = 0;

            if (requesterInfo.paramDic.TryGetValue(eGuildParamType.GuildId, out guildParam))
            {
                if (guildParam != null)
                {
                    guildId = Convert.ToInt64(guildParam.value);
                }
            }

            var guildModel = GetGuildModelByGuildId(guildId, out onlineCharId);

            if (guildModel != null && guildModel.GetGuildState() != eGuildState.Dissolved && onlineCharId > 0)
            {
                switch (type)
                {
                    case eGuildActionType.MemberIntroduceChange:
                        result = ReqGuildMemberIntroduceChange(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.NoticeChange:
                        result = ReqGuildNoticeChange(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.MemberGradeChange:
                        result = ReqGuildMemberGradeChange(requesterInfo, guildModel);
                        break;
                    case eGuildActionType.SettingChange:
                        result = ReqGuildSettingChange(requesterInfo, guildModel);
                        break;
                    default:
                        result = ePacketCommonResult.InternalError;
                        break;
                }
            }
            else
            {
                result = ePacketCommonResult.NotGuildJoined;
                SendGuildActionResult(result, type, requesterInfo, null);
            }

            #region [로그] 81003 - 운영툴 요청 처리 결과
            SLogManager.Instance.GameLog(81003, null
                , new GameLogParameter("guildId", "" + guildId)
                , new GameLogParameter("onlineCharId", "" + onlineCharId)
                , new GameLogParameter("serverGroupId", "" + ServerModule.Instance.GetMyServerGroupId())
                , new GameLogParameter("serverId", "" + requesterInfo.sourceServerId)
                , new GameLogParameter("requestType", "" + type)
                , new GameLogParameter("packet", "" + JsonConvert.SerializeObject(requesterInfo))
                , new GameLogParameter("result", "" + result.ToString())
                );

            #endregion

            return result;
        }

        private ePacketCommonResult ReqGuildJoinAccept(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = ePacketCommonResult.Success;

            do
            {
                if (requesterInfo.GetParamValue(eGuildParamType.TargetCharId, out long targetCharId) == false)
                {
                    SLogManager.Instance.ErrorLog("ReqGuildJoinAccept()", "invalid param - requesterCharId:{0}, guildId:{1}, guildName:{2}", requesterInfo.charId, guildModel.guildId, guildModel.guildName);
                    result = ePacketCommonResult.InvalidGuildParam;
                    break;
                }

                if (_guildJoinByCharIdDic.TryGetValue(targetCharId, out var recruitDic) == false)
                {
                    SLogManager.Instance.ErrorLog("ReqGuildJoinAccept()", "not found guild join request1 - charId:{0}, guildId:{1}, guildName:{2}", targetCharId, guildModel.guildId, guildModel.guildName);
                    result = ePacketCommonResult.CannotJoinGuild;
                    break;
                }

                if (recruitDic.TryGetValue(guildModel.guildId, out var recruit) == false)
                {
                    SLogManager.Instance.ErrorLog("ReqGuildJoinAccept()", "not found guild join request2 - charId:{0}, guildId:{1}, guildName:{2}", targetCharId, guildModel.guildId, guildModel.guildName);
                    result = ePacketCommonResult.CannotJoinGuild;
                    break;
                }

                //else if (recruit.regDate.AddDays(K2Const.MAX_GUILD_JOIN_REQUEST_VALID_DAY) < K2Common.GetDateTime())
                if (recruit.IsValidPeriod(K2Common.GetDateTime()) == false)
                {
                    SLogManager.Instance.ErrorLog("ReqGuildJoinAccept()", "join request time over - charId:{0}, guildId:{1}, guildName:{2}, regDate:{3}", targetCharId, guildModel.guildId, guildModel.guildName, recruit.regDate);
                    result = ePacketCommonResult.NotFoundGuildJoinRequest;
                    break;
                }

                result = IsEnableGuildJoin(eGuildJoinType.None, targetCharId, guildModel);
                if (result == ePacketCommonResult.Success)
                {
                    var memberEntity = new GuildMemberEntity();
                    memberEntity.Init(recruit.charId, recruit.charName, recruit.charTableId, guildModel.guildId, 5);

                    SendZMQGuildInfoSyncSave(eGuildInfoSyncSaveType.JoinAccept, guildModel.guildId, requesterInfo, null, memberEntity, null, null);
                }
            }
            while (false);

            if (result != ePacketCommonResult.Success)
            {
                SendGuildActionResult(result, eGuildActionType.JoinAccept, requesterInfo, null);
            }

            return result;
        }

        private ePacketCommonResult ReqGuildJoinReject(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = ePacketCommonResult.Success;
            if (requesterInfo.GetParamValue(eGuildParamType.TargetCharId, out long targetCharId) == false)
            {
                SLogManager.Instance.ErrorLog($"invalid param - requesterCharId:{requesterInfo.charId}, guildId:{guildModel.guildId}, guildName:{guildModel.guildName}");
                result = ePacketCommonResult.InvalidGuildParam;
            }
            else if (_guildJoinByCharIdDic.TryGetValue(targetCharId, out var recruitDic) == false)
            {
                SLogManager.Instance.ErrorLog($"not fount guild join request1 - charId:{targetCharId}, guildId:{guildModel.guildId}, guildName:{guildModel.guildName}");
                result = ePacketCommonResult.NotFoundGuildJoinRequest;
            }
            else if (recruitDic.TryGetValue(guildModel.guildId, out var recruitEntity) == false)
            {
                SLogManager.Instance.ErrorLog($"not fount guild join request2 - charId:{targetCharId}, guildId:{guildModel.guildId}, guildName:{guildModel.guildName}");
                result = ePacketCommonResult.NotFoundGuildJoinRequest;
            }
            else if (recruitEntity.guildId != guildModel.guildId)
            {
                SLogManager.Instance.ErrorLog($"diff guild join request - charId:{targetCharId}, guildId:{guildModel.guildId}/{recruitEntity.guildId}, guildName:{guildModel.guildName}");
                result = ePacketCommonResult.NotFoundGuildJoinRequest;
            }
            else
            {
                var clone = recruitEntity.Clone();
                clone.Delete();

                SendZMQGuildInfoSyncSave(eGuildInfoSyncSaveType.JoinReject, guildModel.guildId, requesterInfo, null, null, clone, null);
            }

            if (result != ePacketCommonResult.Success)
            {
                SendGuildActionResult(result, eGuildActionType.JoinReject, requesterInfo, null);
            }

            return result;
        }

        private ePacketCommonResult ReqGuildBanish(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = ePacketCommonResult.Success;
            if (requesterInfo.GetParamValue(eGuildParamType.TargetCharId, out long targetCharId) == false)
            {
                SLogManager.Instance.ErrorLog($"invalid param - requesterCharId:{requesterInfo.charId}, guildId:{guildModel.guildId}, guildName:{guildModel.guildName}");
                result = ePacketCommonResult.InvalidGuildParam;
            }
            else
            {
                result = guildModel.ReqGuildBanish(requesterInfo.charId, targetCharId, out GuildMemberEntity banishMember, out GuildRecruitEntity penaltyRecruit);
                if (result == ePacketCommonResult.Success)
                {
                    SendZMQGuildInfoSyncSave(eGuildInfoSyncSaveType.Banish, guildModel.guildId, requesterInfo, null, banishMember, penaltyRecruit, null);
                }
            }

            if (result != ePacketCommonResult.Success)
            {
                SendGuildActionResult(result, eGuildActionType.Banish, requesterInfo, null);
            }

            return result;
        }

        private ePacketCommonResult ReqGuildLeave(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = guildModel.ReqGuildLeave(requesterInfo.charId, out GuildMemberEntity leaveMember, out GuildRecruitEntity penatyRecruit);
            if (result == ePacketCommonResult.Success)
            {
                SendZMQGuildInfoSyncSave(eGuildInfoSyncSaveType.Leave, guildModel.guildId, requesterInfo, null, leaveMember, penatyRecruit, null);
            }
            else
            {
                SendGuildActionResult(result, eGuildActionType.Leave, requesterInfo, null);
            }

            return result;
        }

        /// <summary>
        /// 길드 해산 요청
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <returns></returns>
        private ePacketCommonResult ReqGuildDissolve(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = guildModel.ReqGuildDissolve(requesterInfo.charId,
                                                    out GuildEntity dissolveGuild,
                                                    out GuildMemberEntity leaveMember,
                                                    out GuildRecruitEntity penaltyRecruit,
                                                    out List<GuildRelationEntity> relaionList);
            if (result != ePacketCommonResult.Success)
            {
                SendGuildActionResult(result, eGuildActionType.Dissolve, requesterInfo, null);
                return result;
            }

            var otherGuildRelationList = new List<GuildRelationEntity>();
            foreach (var relation in relaionList)
            {
                switch ((eGuildRelationType)relation.relationType)
                {
                    case eGuildRelationType.Alliance:
                    case eGuildRelationType.AllianceAccept:
                    case eGuildRelationType.AllianceInvite:
                        if (_guildDic.TryGetValue(relation.targetId, out GuildCommunityModel targetGuildModel) == true)
                        {
                            // 다른 길드의 관계인 경우 async 처리
                            targetGuildModel.RemoveGuildRelation(guildModel.guildId, out var relationEntity);
                            targetGuildModel.Modify();
                            otherGuildRelationList.Add(relationEntity);
                        }
                        break;
                }
            }

            SendZMQGuildInfoSyncSave(eGuildInfoSyncSaveType.Dissolve, dissolveGuild.id, requesterInfo, dissolveGuild, leaveMember, penaltyRecruit, relaionList);

            if (0 < otherGuildRelationList.Count)
            {
                SendZMQGuildActionResultByRelation(ePacketCommonResult.Success, eGuildActionType.RejectAlliance, null, otherGuildRelationList);
            }

            // 상대 길드가 나를 적대한 것은 여기서 알 수 없으므로 따로 요청
            var packet = new CS_ZMQ_GuildDeleteRelation();
            packet.targetGuildId = guildModel.guildId;
            packet.relationType = eGuildRelationType.Hostile;
            packet.targetGuildName = guildModel.guildName;
            packet.dissolveCharId = requesterInfo.charId;
            packet.dissolveCharName = requesterInfo.charName;
            ServerModule.Instance.Controller.SendZMQ(eServerType.Database, packet, eServerType.Community);

            return result;
        }

        /// <summary>
        /// 길드 기부 요청
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <returns></returns>
        private ePacketCommonResult ReqGuildGive(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = ePacketCommonResult.Success;
            if (requesterInfo.GetParamValue(eGuildParamType.GiveId, out long giveId) == false)
            {
                SLogManager.Instance.ErrorLog($"invalid param - requesterCharId:{requesterInfo.charId}, guildId:{guildModel.guildId}, guildName:{guildModel.guildName}");
                result = ePacketCommonResult.InvalidGuildParam;
            }
            else
            {
                result = guildModel.ReqGuildGive(requesterInfo.charId, (int)giveId, out var addExp);
                if (addExp > 0)
                    AddGuildExp(guildModel.guildId, addExp);
            }

            SendGuildActionResult(result, eGuildActionType.Give, requesterInfo, guildModel.GetGuildInfo(), guildModel.GetGuildMemberInfo(requesterInfo.charId));

            return result;
        }

        /// <summary>
        /// 길드 문장 요청
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <returns></returns>
        private ePacketCommonResult ReqAddCrest(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = ePacketCommonResult.Success;
            var isOverlapped = false;
            List<GuildCrestEntity> crestEntityList = null;

            if (requesterInfo.GetParamValue(eGuildParamType.CrestId, out long crestId) == false)
            {
                SLogManager.Instance.ErrorLog($"invalid param - requesterCharId:{requesterInfo.charId}, guildId:{guildModel.guildId}, guildName:{guildModel.guildName}");
                result = ePacketCommonResult.InvalidGuildParam;
            }
            else
            {
                result = guildModel.ReqAddCrest(requesterInfo.charId, (int)crestId, out crestEntityList, out isOverlapped, out var addExp);
                if (isOverlapped)
                    AddGuildExp(guildModel.guildId, addExp);

            }

            SendZMQGuildActionResultByCrest(result, eGuildActionType.AddCrest, requesterInfo, (isOverlapped ? guildModel.GetGuildInfo() : null), crestEntityList);

            return result;
        }

        /// <summary>
        /// 길드원 소개글 변경 요청
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <returns></returns>
        private ePacketCommonResult ReqGuildMemberIntroduceChange(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = ePacketCommonResult.Success;
            if (requesterInfo.GetParamValue(eGuildParamType.Introduce, out string introduce) == false)
            {
                SLogManager.Instance.ErrorLog($"invalid param - requesterCharId:{requesterInfo.charId}, guildId:{guildModel.guildId}, guildName:{guildModel.guildName}");
                result = ePacketCommonResult.InvalidGuildParam;
            }
            else
            {
                result = guildModel.ReqGuildMemberIntroduceChange(requesterInfo.charId, introduce);
            }

            SendGuildActionResult(result, eGuildActionType.MemberIntroduceChange, requesterInfo, guildModel.GetGuildInfo(), guildModel.GetGuildMemberInfo(requesterInfo.charId));
            return result;
        }

        /// <summary>
        /// 길드 멤버의 퀘스트 리셋 값 변경
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <returns></returns>
        private ePacketCommonResult ReqGuildMemberQuestInfoChange(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = ePacketCommonResult.Success;
            if (requesterInfo.GetParamValue(eGuildParamType.QuestInit, out string isQuestInit) == false)
            {
                SLogManager.Instance.ErrorLog($"invalid param - requesterCharId:{requesterInfo.charId}, guildId:{guildModel.guildId}, guildName:{guildModel.guildName}");
                result = ePacketCommonResult.InvalidGuildParam;
            }
            else
            {
                result = guildModel.ReqGuildMemberQuestInitChange(requesterInfo.charId, isQuestInit);
            }

            SendGuildActionResult(result, eGuildActionType.MemberQuestInit, requesterInfo, guildModel.GetGuildInfo(), guildModel.GetGuildMemberInfo(requesterInfo.charId));
            return result;
        }

        /// <summary>
        /// 길드 문자 변경 요청
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <returns></returns>
        private ePacketCommonResult ReqGuildCrestChange(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = ePacketCommonResult.Success;
            if (requesterInfo.GetParamValue(eGuildParamType.CrestId, out long crestId) == false)
            {
                SLogManager.Instance.ErrorLog($"invalid param - requesterCharId:{requesterInfo.charId}, guildId:{guildModel.guildId}, guildName:{guildModel.guildName}");
                result = ePacketCommonResult.InvalidGuildParam;
            }
            else
            {
                result = guildModel.ReqGuildCrestChange(requesterInfo.charId, (int)crestId);
            }

            SendGuildActionResult(result, eGuildActionType.CrestChange, requesterInfo, guildModel.GetGuildInfo(), null);
            return result;
        }

        /// <summary>
        /// 길드 공지 변경 요청
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <returns></returns>
        private ePacketCommonResult ReqGuildNoticeChange(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = ePacketCommonResult.Success;
            if (requesterInfo.GetParamValue(eGuildParamType.Notice, out string notice) == false)
            {
                SLogManager.Instance.ErrorLog($"invalid param - requesterCharId:{requesterInfo.charId}, guildId:{guildModel.guildId}, guildName:{guildModel.guildName}");
                result = ePacketCommonResult.InvalidGuildParam;
            }
            else
            {
                result = guildModel.ReqGuildNoticeChange(requesterInfo.charId, notice);
            }

            SendGuildActionResult(result, eGuildActionType.NoticeChange, requesterInfo, guildModel.GetGuildInfo(), null);

            if (result == ePacketCommonResult.Success)
            {
                SendZMQGuildNotify(eGuildNotifyType.GuildNoticeChange, guildModel.guildId, 0, string.Empty, notice);
            }

            return ePacketCommonResult.Success;
        }

        /// <summary>
        /// 길드원 등급 변경 요청
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <returns></returns>
        private ePacketCommonResult ReqGuildMemberGradeChange(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = ePacketCommonResult.Success;

            if (requesterInfo.GetParamValue(eGuildParamType.TargetCharId, out long targetCharId) == false)
            {
                SLogManager.Instance.ErrorLog("ReqGuildMemberGradeChange()", "invalid param - requesterCharId:{0}, guildId:{1}, guildName:{2}",
                    requesterInfo.charId, guildModel.guildId, guildModel.guildName);
                return ePacketCommonResult.InvalidGuildParam;
            }

            if (requesterInfo.GetParamValue(eGuildParamType.MemberGrade, out long grade) == false)
            {
                SLogManager.Instance.ErrorLog("ReqGuildMemberGradeChange()", "invalid param - requesterCharId:{0}, guildId:{1}, guildName:{2}",
                    requesterInfo.charId, guildModel.guildId, guildModel.guildName);
                return ePacketCommonResult.InvalidGuildParam;
            }

            result = guildModel.ReqGuildMemberGradeChange(requesterInfo.charId, targetCharId, (int)grade);
            if (result != ePacketCommonResult.Success)
            {
                SendGuildActionResult(result, eGuildActionType.MemberGradeChange, requesterInfo, null);
                return result;
            }

            var guildInfo = new GuildInfoData();

            guildInfo.guild = guildModel.GetGuildInfo();

            guildInfo.memberList.Add(guildModel.GetGuildMemberList().FirstOrDefault(m => m.charId == requesterInfo.charId));
            guildInfo.memberList.Add(guildModel.GetGuildMemberList().FirstOrDefault(m => m.charId == targetCharId));

            requesterInfo.paramDic[eGuildParamType.TargetCharId].strValue = grade.ToString();

            SendGuildActionResult(result, eGuildActionType.MemberGradeChange, requesterInfo, guildInfo);

            return result;
        }

        /// <summary>
        /// 길드 설정 변경 요청
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <returns></returns>
        private ePacketCommonResult ReqGuildSettingChange(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = ePacketCommonResult.Success;
            if (requesterInfo.paramDic.Count <= 0)
            {
                SLogManager.Instance.ErrorLog($"invalid param - requesterCharId:{requesterInfo.charId}, guildId:{guildModel.guildId}, guildName:{guildModel.guildName}");
                result = ePacketCommonResult.InvalidGuildParam;
            }
            else
            {
                result = guildModel.ReqGuildSettingChange(requesterInfo);
            }

            SendGuildActionResult(result, eGuildActionType.SettingChange, requesterInfo, guildModel.GetGuildInfo(), null);

            return result;
        }

        /// <summary>
        /// 길드 동맹 요청
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <param name="needDBReq"></param>
        /// <returns></returns>
        private ePacketCommonResult ReqGuildAlliance(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = ePacketCommonResult.Success;
            List<GuildRelationEntity> resultEntityList = null;

            do
            {
                if (requesterInfo.GetParamValue(eGuildParamType.GuildName, out string targetGuildName) == false)
                {
                    SLogManager.Instance.ErrorLog("ReqGuildAlliance()", "invalid param - requesterCharId:{0}, guildId:{1}, guildName:{2}",
                        requesterInfo.charId, guildModel.guildId, guildModel.guildName);
                    result = ePacketCommonResult.InvalidGuildParam;
                    break;
                }

                if (_guildByNameDic.TryGetValue(targetGuildName, out var targetGuildModel) == false)
                {
                    //SendZMQGuildInfoQuery(eGuildLoadType.GuildReqAlliance, 0, 0, requesterInfo);
                    result = ePacketCommonResult.NotFoundGuild;
                    break;
                }

                if (targetGuildModel.GetGuildState() == eGuildState.Dissolved)
                {
                    result = ePacketCommonResult.NotFoundGuild;
                    break;
                }

                result = guildModel.ReqGuildAlliance(requesterInfo.charId, targetGuildModel, out resultEntityList);

                requesterInfo.paramDic[eGuildParamType.GuildName].value = targetGuildModel.guildId;
            }
            while (false);

            SendZMQGuildActionResultByRelation(result, eGuildActionType.ReqAlliance, requesterInfo, resultEntityList);

            return result;
        }

        /// <summary>
        /// 길드 동맹 수락 요청
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <returns></returns>
        private ePacketCommonResult ReqGuildAllianceAccept(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = ePacketCommonResult.Success;
            List<GuildRelationEntity> resultEntityList = null;

            do
            {
                if (requesterInfo.GetParamValue(eGuildParamType.GuildId, out long targetGuildId) == false)
                {
                    SLogManager.Instance.ErrorLog("ReqGuildAllianceAccept()", "invalid param - requesterCharId:{0}, guildId:{1}, guildName:{2}",
                        requesterInfo.charId, guildModel.guildId, guildModel.guildName);
                    result = ePacketCommonResult.InvalidGuildParam;
                    break;
                }

                if (IsExistLoadingGuild(guildModel.guildId, targetGuildId))
                {
                    result = ePacketCommonResult.LoadingGuildInfo;
                    break;
                }

                if (_guildDic.TryGetValue(targetGuildId, out var targetGuildModel) == false)
                {
                    // 길드 로딩 시 관계있는 길드까지 로드되기 때문에 나와서는 안되는 에러
                    SLogManager.Instance.ErrorLog("ReqGuildAllianceAccept()", "cannot found guild info - requesterCharId:{0}, guildId:{1}",
                        requesterInfo.charId, targetGuildId);
                    result = ePacketCommonResult.InternalError;
                    break;
                }

                if (targetGuildModel.GetGuildState() == eGuildState.Dissolved)
                {
                    result = ePacketCommonResult.NotFoundGuild;
                    break;
                }

                result = guildModel.ReqGuildAllianceAccept(requesterInfo.charId, targetGuildModel, out resultEntityList);

                requesterInfo.paramDic[eGuildParamType.GuildId].strValue = targetGuildModel.guildName;
                requesterInfo.paramDic[eGuildParamType.GuildName].value = targetGuildModel.guildId;
            }
            while (false);

            SendZMQGuildActionResultByRelation(result, eGuildActionType.AcceptAlliance, requesterInfo, resultEntityList);

            return result;
        }

        /// <summary>
        /// 길드 동맹 거절 요청
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <returns></returns>
        private ePacketCommonResult ReqGuildAllianceReject(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = ePacketCommonResult.Success;
            List<GuildRelationEntity> resultEntityList = null;

            do
            {
                if (requesterInfo.GetParamValue(eGuildParamType.GuildId, out long targetGuildId) == false)
                {
                    SLogManager.Instance.ErrorLog("ReqGuildAllianceReject()", "invalid param - requesterCharId:{0}, guildId:{1}, guildName:{2}",
                        requesterInfo.charId, guildModel.guildId, guildModel.guildName);
                    result = ePacketCommonResult.InvalidGuildParam;
                    break;
                }

                if (IsExistLoadingGuild(guildModel.guildId, targetGuildId))
                {
                    result = ePacketCommonResult.LoadingGuildInfo;
                    break;
                }

                if (_guildDic.TryGetValue(targetGuildId, out var targetGuildModel) == false)
                {
                    // 길드 로딩 시 관계있는 길드까지 로드되기 때문에 나와서는 안되는 에러
                    SLogManager.Instance.ErrorLog("ReqGuildAllianceReject()", "cannot found guild info - requesterCharId:{0}, guildId:{1}",
                        requesterInfo.charId, targetGuildId);
                    result = ePacketCommonResult.InternalError;
                    break;
                }

                if (targetGuildModel.GetGuildState() == eGuildState.Dissolved)
                {
                    result = ePacketCommonResult.NotFoundGuild;
                    break;
                }

                result = guildModel.ReqGuildAllianceReject(requesterInfo.charId, targetGuildModel, out resultEntityList);

                requesterInfo.paramDic[eGuildParamType.GuildId].strValue = targetGuildModel.guildName;
                requesterInfo.paramDic[eGuildParamType.GuildName].value = targetGuildModel.guildId;
            }
            while (false);

            SendZMQGuildActionResultByRelation(result, eGuildActionType.RejectAlliance, requesterInfo, resultEntityList);

            return result;
        }

        /// <summary>
        /// 길드 동맹 삭제 요청
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <returns></returns>
        private ePacketCommonResult ReqGuildAllianceRemove(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = ePacketCommonResult.Success;
            List<GuildRelationEntity> resultEntityList = null;

            do
            {
                if (requesterInfo.GetParamValue(eGuildParamType.GuildId, out long targetGuildId) == false)
                {
                    SLogManager.Instance.ErrorLog("ReqGuildAllianceRemove()", "invalid param - requesterCharId:{0}, guildId:{1}, guildName:{2}",
                        requesterInfo.charId, guildModel.guildId, guildModel.guildName);
                    result = ePacketCommonResult.InvalidGuildParam;
                    break;
                }

                if (IsExistLoadingGuild(guildModel.guildId, targetGuildId))
                {
                    result = ePacketCommonResult.LoadingGuildInfo;
                    break;
                }

                if (_guildDic.TryGetValue(targetGuildId, out var targetGuildModel) == false)
                {
                    // 길드 로딩 시 관계있는 길드까지 로드되기 때문에 나와서는 안되는 에러
                    SLogManager.Instance.ErrorLog("ReqGuildAllianceRemove()", "cannot found guild info - requesterCharId:{0}, guildId:{1}",
                        requesterInfo.charId, targetGuildId);
                    result = ePacketCommonResult.InternalError;
                    break;
                }

                if (targetGuildModel.GetGuildState() == eGuildState.Dissolved)
                {
                    result = ePacketCommonResult.NotFoundGuild;
                    break;
                }

                result = guildModel.ReqGuildAllianceRemove(requesterInfo.charId, targetGuildModel, out resultEntityList);

                requesterInfo.paramDic[eGuildParamType.GuildId].strValue = targetGuildModel.guildName;
                requesterInfo.paramDic[eGuildParamType.GuildName].value = targetGuildModel.guildId;
            }
            while (false);

            SendZMQGuildActionResultByRelation(result, eGuildActionType.RemoveAlliance, requesterInfo, resultEntityList);

            return result;
        }

        /// <summary>
        /// 적대 길드 등록 요청
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <param name="needDBReq"></param>
        /// <returns></returns>
        private ePacketCommonResult ReqGuildAddHostile(GuildRequesterData requesterInfo, GuildCommunityModel guildModel, bool needDBReq)
        {
            var result = ePacketCommonResult.Success;
            List<GuildRelationEntity> resultEntityList = null;

            do
            {
                if (requesterInfo.GetParamValue(eGuildParamType.GuildName, out string targetGuildName) == false)
                {
                    SLogManager.Instance.ErrorLog("ReqGuildAddHostile()", "invalid param - requesterCharId:{0}, guildId:{1}, guildName:{2}",
                        requesterInfo.charId, guildModel.guildId, guildModel.guildName);
                 
                    result = ePacketCommonResult.InvalidGuildParam;
                    break;
                }

                if (_guildByNameDic.TryGetValue(targetGuildName, out var targetGuildModel) == false)
                {
                    // 모든 길드 정보가 로딩되므로 dic에 없다면 존재하지 않는 길드다.
                    SLogManager.Instance.InfoLog("ReqGuildAddHostile()", "cannot found guild info - requesterCharId:{0}, guildName:{1}",
                        requesterInfo.charId, targetGuildName);
                    
                    result = ePacketCommonResult.NotFoundGuild;
                    break;

                    //if (needDBReq)
                    //{
                    //    SendZMQGuildInfoQuery(eGuildLoadType.GuildAddHostile, 0, 0, requesterInfo);
                    //}
                    //else
                    //{
                    //    // 나와서는 안되는 에러
                    //    SLogManager.Instance.ErrorLog("ReqGuildAddHostile()", "cannot found guild info - requesterCharId:{0}, guildName:{1}",
                    //        requesterInfo.charId, targetGuildName);
                    //    result = ePacketCommonResult.InternalError;
                    //}
                }

                if (targetGuildModel.GetGuildState() == eGuildState.Dissolved)
                {
                    result = ePacketCommonResult.NotFoundGuild;
                    break;
                }
                else
                {
                    result = guildModel.ReqGuildAddHostile(requesterInfo.charId, targetGuildModel, out resultEntityList);
                    break;
                }
            }
            while (false);

            SendZMQGuildActionResultByRelation(result, eGuildActionType.AddHostile, requesterInfo, resultEntityList);

            return result;
        }

        /// <summary>
        /// 적대 길드 삭제 요청
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <returns></returns>
        private ePacketCommonResult ReqGuildRemoveHostile(GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var result = ePacketCommonResult.Success;
            List<GuildRelationEntity> resultEntityList = null;

            do
            {
                if (requesterInfo.GetParamValue(eGuildParamType.GuildId, out long targetGuildId) == false)
                {
                    SLogManager.Instance.ErrorLog("ReqGuildRemoveHostile()", "invalid param - requesterCharId:{0}, guildId:{1}, guildName:{2}",
                        requesterInfo.charId, guildModel.guildId, guildModel.guildName);
                    result = ePacketCommonResult.InvalidGuildParam;
                    break;
                }

                if (IsExistLoadingGuild(guildModel.guildId, targetGuildId))
                {
                    result = ePacketCommonResult.LoadingGuildInfo;
                    break;
                }

                if (_guildDic.TryGetValue(targetGuildId, out var targetGuildModel) == false)
                {
                    // 길드 로딩 시 관계있는 길드까지 로드되기 때문에 나와서는 안되는 에러
                    SLogManager.Instance.ErrorLog("ReqGuildRemoveHostile()", "cannot found guild info - requesterCharId:{0}, guildId:{1}",
                        requesterInfo.charId, targetGuildId);
                    result = ePacketCommonResult.InternalError;
                    break;

                }

                if (targetGuildModel.GetGuildState() == eGuildState.Dissolved)
                {
                    result = ePacketCommonResult.NotFoundGuild;
                    break;
                }

                result = guildModel.ReqGuildRemoveHostile(requesterInfo.charId, targetGuildModel, out resultEntityList);
            }
            while (false);

            SendZMQGuildActionResultByRelation(result, eGuildActionType.RemoveHostile, requesterInfo, resultEntityList);

            return result;
        }

        /// <summary>
        /// 척살 유저 등록 요청
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <param name="enermyInfo"></param>
        /// <returns></returns>
        public ePacketCommonResult ReqGuildAddEnermy(GuildRequesterData requesterInfo, GuildCommunityModel guildModel, GuildEnermyInfoPacket enermyInfo)
        {
            var result = ePacketCommonResult.Success;

            do
            {
                if (requesterInfo.GetParamValue(eGuildParamType.TargetCharName, out string targetCharName) == false)
                {
                    SLogManager.Instance.ErrorLog($"invalid param - requesterCharId:{requesterInfo.charId}, guildId:{guildModel.guildId}, guildName:{guildModel.guildName}");
                    result = ePacketCommonResult.InvalidGuildParam;
                    break;
                }

                if (enermyInfo == null)
                {
                    var userBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(targetCharName);
                    if (userBBModel == null)
                    {
                        SendZMQSearchCharacterNameByGuild(eGuildActionType.AddEnermy, requesterInfo);
                        break;
                    }
                    else
                    {
                        enermyInfo = userBBModel.Extraction();
                    }
                }

                result = guildModel.ReqGuildAddEnermy(requesterInfo.charId, enermyInfo, out var resultEntityList);
                SendZMQGuildActionResultByRelation(result, eGuildActionType.AddEnermy, requesterInfo, resultEntityList);

            } while (false);

            return result;
        }

        /// <summary>
        /// 척살 유저 삭제 요청
        /// </summary>
        /// <param name="requesterInfo"></param>
        /// <param name="guildModel"></param>
        /// <param name="enermyInfo"></param>
        /// <returns></returns>
        public ePacketCommonResult ReqGuildRemoveEnemy(GuildRequesterData requesterInfo, GuildCommunityModel guildModel, GuildEnermyInfoPacket enermyInfo)
        {
            var result = ePacketCommonResult.Success;
            List<GuildRelationEntity> resultEntityList = null;

            do
            {
                if (requesterInfo.GetParamValue(eGuildParamType.TargetCharName, out string targetCharName) == false)
                {
                    SLogManager.Instance.ErrorLog($"invalid param - requesterCharId:{requesterInfo.charId}, guildId:{guildModel.guildId}, guildName:{guildModel.guildName}");
                    result = ePacketCommonResult.InvalidGuildParam;
                    break;
                }

                if (string.IsNullOrEmpty(targetCharName))
                {
                    SLogManager.Instance.ErrorLog($"ReqGuildRemoveEnermy failed, invaild targetCharName, value: {targetCharName}", apiSend: true);
                    result = ePacketCommonResult.InvalidGuildParam;
                    break;
                }

                if (enermyInfo == null)
                {
                    var userBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(targetCharName);
                    if (userBBModel == null)
                    {
                        SendZMQSearchCharacterNameByGuild(eGuildActionType.RemoveEnermy, requesterInfo);
                        break;
                    }
                    else
                    {
                        enermyInfo = userBBModel.Extraction();
                    }
                }

                result = guildModel.ReqGuildRemoveEmermy(requesterInfo.charId, enermyInfo, out resultEntityList);
                SendZMQGuildActionResultByRelation(result, eGuildActionType.RemoveEnermy, requesterInfo, resultEntityList);

            } while (false);

            return result;
        }

        /// <summary>
        /// 길드 관계 (동맹, 적대) 삭제 요청
        /// </summary>
        /// <param name="guildIdList"></param>
        /// <param name="targetGuildId"></param>
        /// <param name="relationType"></param>
        /// <returns></returns>
        public ePacketCommonResult ReqGuildDeleteRelation(List<long> guildIdList, long targetGuildId, eGuildRelationType relationType, long dissolveCharId, string dissolveCharName, string targetGuildName)
        {
            var result = ePacketCommonResult.Success;
            var relationList = new List<GuildRelationEntity>();

            // 오프라인 처리를 위해 이미 db에서 삭제처리했으므로 컨테이너에서 삭제만 한다
            foreach (var guildId in guildIdList)
            {
                if (_guildDic.TryGetValue(guildId, out GuildCommunityModel guildModel) == false)
                {
                    continue;
                }

                if (guildModel.GetGuildRelation(relationType, targetGuildId, out var relationEntity) == false)
                {
                    continue;
                }

                var clone = relationEntity.Clone();
                clone.Delete();
                relationList.Add(clone);
                guildModel.RemoveRelationEntity(relationEntity);
            }

            switch (relationType)
            {
                case eGuildRelationType.Enermy:
                    {
                        var userBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(targetGuildId);
                        if (userBBModel != null)
                        {
                            userBBModel.delayedEvent.RunAfterDeleyedEvent(eDelayedEventKindType.BlackBoardCharacterDelete, eBlackBoardEventType.GuildEnermy);
                        }

                        SendZMQGuildActionResultByRelation(result, eGuildActionType.RemoveEnermy, null, relationList);
                    }
                    break;

                case eGuildRelationType.Hostile:
                    {
                        GuildRequesterData guildRequesterData = new GuildRequesterData();
                        guildRequesterData.charId = dissolveCharId;
                        guildRequesterData.charName = dissolveCharName;

                        //var tarGetGuildInfo = new GuildParam(eGuildParamType.DissolvedHostileGuild, targetGuildId, targetGuildName);
                        //guildRequesterData.paramDic.Add(eGuildParamType.DissolvedHostileGuild, tarGetGuildInfo);

                        SendZMQGuildActionResultByDissolvedRelation(result, eGuildActionType.RemoveHostile, guildRequesterData, relationList);
                    }
                    break;
            }

            return result;
        }

        public void AddGuildExp(long guildId, long exp)
        {
            if (_guildDic.TryGetValue(guildId, out GuildCommunityModel guildModel) == false)
            {
                SLogManager.Instance.ErrorLog($"guildId({guildId}) , exp({exp})");
                return;
            }

            guildModel.AddExp(exp, out var isLevelUp);
            if (isLevelUp)
            {
                if (guildModel.CheckPlaceExpand())
                    ClearGuildPlaceZone(guildModel.guildId, eZoneForceChangeType.GuildPlace_Upgrade);

                if (guildModel.CheckAddGuildCrest())
                    SendZMQGuildNotify(eGuildNotifyType.OpenCrestByLevel, guildModel.guildId, 0, string.Empty, string.Empty);
            }

            guildModel.Modify(isLevelUp);
        }

        public void ReqGuildExpSync(long charId, long addExp)
        {
            var guildModel = GetGuildModel(charId);
            if (guildModel != null)
            {
                var memberModel = guildModel.GetGuildMemberInfo(charId);
                if (memberModel == null)
                    return;

                memberModel.CheckRefresh(K2Common.GetDateTime());
                memberModel.AddContribution(addExp);

                AddGuildExp(guildModel.guildId, addExp);
            }
        }

        private void CheatAddGuildLevel(GuildModel guild, int level)
        {
            if (level <= 0)
                return;

            var guildLevelDataManager = DataManager.Get<LoaderGuildLevel>();

            int prevLevel = guild.GetGuildInfo().level;
            var nextlevel = prevLevel + level;
            long totalAddExp = 0;
            for (int i = prevLevel; i < nextlevel; ++i)
            {
                totalAddExp += guildLevelDataManager.GetGuildExp(i);
            }

            AddGuildExp(guild.guildId, totalAddExp);
        }

        public void ReqGuildCheat(eCheatCommand cheatCmd, GuildRequesterData requesterInfo)
        {
            var guildModel = GetGuildModel(requesterInfo.charId);
            if (guildModel != null)
            {
                RankingEntity rankingInfo = null;

                switch (cheatCmd)
                {
                    case eCheatCommand.guildmaxmember:
                        requesterInfo.GetParamValue(eGuildParamType.CheatMaxMember, out long maxMemberCount);
                        guildModel.SetCheatMaxMemberCount((int)maxMemberCount);
                        break;
                    case eCheatCommand.guildaddexp:
                        requesterInfo.GetParamValue(eGuildParamType.CheatAddExp, out long addExp);
                        AddGuildExp(guildModel.guildId, addExp);

                        RankingCommunityManager.Instance.ReqMyRankingInfo(eRankingType.Guild, 0, guildModel.guildId, ref rankingInfo);
                        break;
                    case eCheatCommand.guildaddlevel:
                        requesterInfo.GetParamValue(eGuildParamType.CheatSetLevel, out long level);
                        CheatAddGuildLevel(guildModel, (int)level);

                        RankingCommunityManager.Instance.ReqMyRankingInfo(eRankingType.Guild, 0, guildModel.guildId, ref rankingInfo);
                        break;
                }

                if (rankingInfo != null)
                {
                    // 치트로 스코어(레벨 및 경험치)가 변경된 길드는 랭킹에 적용시킨다.
                    var guildEntity = guildModel.GetGuildInfo();
                    var levelData = DataManager.Get<LoaderGuildLevel>().GetGuildLevelData(guildEntity.level);
                    var guildMaxcount = (levelData == null) ? 0 : levelData.Member;

                    var rankingInfoClone = rankingInfo.CopyTo();
                    rankingInfoClone.PackingGuildRanking(guildEntity.level, guildEntity.masterName, guildEntity.crestId, guildEntity.memberCount, guildMaxcount, guildEntity.introduce);
                    rankingInfoClone.score = guildEntity.exp;

                    var cmd = new RankingUpdateCmd()
                    {
                        rankingType = eRankingType.Guild
                    };
                    cmd.rankingInfoList.Add(rankingInfoClone);

                    RankingCommunityManager.Instance.AddCommand(cmd);
                }
            }
            else
            {
                SLogManager.Instance.ErrorLog($"invalid cheatCommand - charId:{requesterInfo.charId}, cheatCmd:{cheatCmd}");
            }
        }

        /// <summary>
        /// 길드원 직업(Class) 변경 요청
        /// </summary>
        /// <param name="charId"></param>
        /// <param name="changeClass"></param>
        public void ReqGuildMemberClassChange(long charId, eCharacterClass changeClass)
        {
            var guildModel = GetGuildModel(charId);
            if (guildModel == null)
            {
                return;
            }

            var charBaseData = DataManager.Get<LoaderCharacterBase>().GetCharacterBaseForClassPC(changeClass);
            if (charBaseData == null)
            {
                SLogManager.Instance.ErrorLog($"not exist characterBase data - charId:{charId}, characterClass:{changeClass}");
                return;
            }

            var guildEntity = guildModel.GetGuildInfo();
            if (guildEntity.masterCharId == charId)
            {
                guildEntity.masterCharTableId = (int)changeClass;
                DbEntityManager.UpdateEntity(guildEntity);

                // guild ranking update
                RankingCommunityManager.Instance.ReqGuildMasterClassChange(guildEntity.id, guildEntity.masterCharTableId);
            }

            guildModel.ReqGuildMemberClassChange(charId, charBaseData.Id);
        }

        /// <summary>
        /// 길드 지원자 직업(Class) 변경 요청
        /// </summary>
        /// <param name="charId"></param>
        /// <param name="changeClass"></param>
        public void ReqGuildRecruitMemberClassChange(long charId, eCharacterClass changeClass)
        {
            var charBaseData = DataManager.Get<LoaderCharacterBase>().GetCharacterBaseForClassPC(changeClass);
            if (charBaseData == null)
            {
                SLogManager.Instance.ErrorLog("ReqGuildRecruitMemberClassChange()",
                    "not exist characterBase data. charId({0}), characterClass({1})",
                    charId, changeClass);
                return;
            }

            // <charId, <guildId, GuildRecruitEntity>> (가입요청) // GuildId:0 으로 추가되어 있을시 재가입 패널티중
            if (_guildJoinByCharIdDic.TryGetValue(charId, out ConcurrentDictionary<long, GuildRecruitEntity> recruitDic) == false)
            {
                SLogManager.Instance.ErrorLog("ReqGuildRecruitMemberClassChange()",
                    "not exist character recruit data. charId({0}), characterClass({1})",
                    charId, changeClass);
                return;
            }

            foreach (var recruit in recruitDic.Values)
            {
                if (recruit == null)
                {
                    continue;
                }

                if (recruit.guildId == 0)
                {
                    continue;
                }

                recruit.charTableId = (int)changeClass;
                DbEntityManager.UpdateEntity(recruit);

                // <guildId, <charId, GuildRecruitEntit>> (가입요청)
                if (_guildJoinByGuildIdDic.TryGetValue(recruit.guildId, out ConcurrentDictionary<long, GuildRecruitEntity> recruitGuildDic) == false)
                {
                    continue;
                }

                if (recruitGuildDic.TryGetValue(charId, out var guildRecruit) == false)
                {
                    continue;
                }

                guildRecruit.charTableId = (int)changeClass;
            }
        }

        /// <summary>
        /// 길드장 인지 확인
        /// </summary>
        /// <param name="charId"></param>
        /// <param name="guildId"></param>
        /// <returns></returns>
        public bool CheckMasterCharacterGuildId(long charId, out long guildId)
        {
            guildId = 0;
            var guildModel = GetGuildModel(charId);
            if (guildModel != null &&
                guildModel.GetGuildInfo().masterCharId == charId)
            {
                guildId = guildModel.guildId;
                return true;
            }

            return false;
        }

        public void SendZMQGuildInfoQuery(eGuildLoadType type, long ownerId, long guildId, GuildRequesterData requesterInfo = null)
        {
            var packet = new CS_ZMQ_GuildLoadQuery();
            packet.type = type;
            packet.ownerId = ownerId;
            packet.guildId = guildId;

            if (requesterInfo != null)
            {
                packet.requesterInfo.Copy(requesterInfo);
            }

            ServerModule.Instance.Controller.SendZMQ(eServerType.Database, packet, eServerType.Community);
        }

        public void SendZMQSearchCharacterNameByGuild(eGuildActionType type, GuildRequesterData requesterInfo)
        {
            var packet = new CS_ZMQ_SearchCharacterNameByGuild();
            packet.actionType = type;
            packet.requesterInfo.Copy(requesterInfo);
            ServerModule.Instance.Controller.SendZMQ(eServerType.Database, packet, eServerType.Community);
        }

        public void SendZMQGuildInfoSyncSave(eGuildInfoSyncSaveType type, long guildId, GuildRequesterData requesterInfo, GuildEntity guildEntity, GuildMemberEntity memberEntity, GuildRecruitEntity recruitEntity,
            List<GuildRelationEntity> relaionList)
        {
            var packet = new CS_ZMQ_GuildInfoSyncSave();

            packet.type = type;

            if (requesterInfo != null)
            {
                packet.requesterInfo.Copy(requesterInfo);
            }

            if (guildEntity != null)
            {
                packet.guildInfo.guild.Copy(guildEntity);
            }

            if (memberEntity != null)
            {
                packet.guildInfo.memberList.Add(memberEntity);
            }

            if (relaionList != null)
            {
                packet.guildInfo.relationList.AddRange(relaionList);
            }

            if (recruitEntity != null)
            {
                packet.recruitList.Add(recruitEntity);
            }

            switch (type)
            {
                case eGuildInfoSyncSaveType.Dissolve:
                case eGuildInfoSyncSaveType.RecruitClearByGuild:
                    ReqAllRecruitInfoDeleteByGuildId(guildId, ref packet.recruitList);
                    break;
                case eGuildInfoSyncSaveType.Create:
                    int guildPlaceId = DataManager.Get<LoaderGuildPlace>().GetGuildPlaceId(packet.guildInfo.guild.level);
                    packet.guildInfo.place.Init(packet.guildInfo.guild.id, guildPlaceId);
                    ReqAllRecruitInfoDeleteByCharId(requesterInfo.charId, guildEntity, ref packet.recruitList);
                    break;
                case eGuildInfoSyncSaveType.JoinDirect:
                case eGuildInfoSyncSaveType.JoinAccept:
                    ReqAllRecruitInfoDeleteByCharId(requesterInfo.charId, guildEntity, ref packet.recruitList);
                    break;
                case eGuildInfoSyncSaveType.JoinRequest:
                    ReqRecruitInfoDeleteByPenalty(requesterInfo.charId, ref packet.recruitList);
                    break;
                case eGuildInfoSyncSaveType.InvalidRequestClearByChar:
                    ReqRecruitInfoDeleteByInvalidRequestChar(requesterInfo.charId, ref packet.recruitList);
                    break;
                case eGuildInfoSyncSaveType.InvalidRequestClearByGuild:
                    ReqRecruitInfoDeleteByInvalidRequestGuild(guildId, ref packet.recruitList);
                    break;
            }

            ServerModule.Instance.Controller.SendZMQ(eServerType.Database, packet, eServerType.Community);
        }

        // recruitEntity 삭제를 위해 수집하는 함수
        private void ReqAllRecruitInfoDeleteByCharId(long charId, GuildEntity guildEntity, ref List<GuildRecruitEntity> recruitList)
        {
            if (_guildJoinByCharIdDic.TryGetValue(charId, out ConcurrentDictionary<long, GuildRecruitEntity> recruitDic) == true)
            {
                foreach (var entity in recruitDic.Values)
                {
                    var clone = entity.Clone();
                    clone.Delete();
                    recruitList.Add(clone);
                }
            }
        }

        private void ReqRecruitInfoDeleteByPenalty(long charId, ref List<GuildRecruitEntity> recruitList)
        {
            if (_guildJoinByCharIdDic.TryGetValue(charId, out ConcurrentDictionary<long, GuildRecruitEntity> recruitDic) == true)
            {
                if (recruitDic.TryGetValue(0, out var entity) == true)
                {
                    var clone = entity.Clone();
                    clone.Delete();
                    recruitList.Add(clone);
                }
            }
        }

        private void ReqAllRecruitInfoDeleteByGuildId(long guildId, ref List<GuildRecruitEntity> recruitList)
        {
            if (_guildJoinByGuildIdDic.TryGetValue(guildId, out ConcurrentDictionary<long, GuildRecruitEntity> recruitDic) == true)
            {
                foreach (var entity in recruitDic.Values)
                {
                    var clone = entity.Clone();
                    clone.Delete();
                    recruitList.Add(clone);
                }
            }
        }

        private void ReqRecruitInfoDeleteByInvalidRequestChar(long charId, ref List<GuildRecruitEntity> recruitList)
        {
            var now = K2Common.GetDateTime();
            if (_guildJoinByCharIdDic.TryGetValue(charId, out ConcurrentDictionary<long, GuildRecruitEntity> recruitDic) == true)
            {
                foreach (var entity in recruitDic.Values)
                {
                    if (entity.IsValidPeriod(now) == true)
                    {
                        continue;
                    }

                    var clone = entity.Clone();
                    clone.Delete();
                    recruitList.Add(clone);
                }
            }
        }

        private void ReqRecruitInfoDeleteByInvalidRequestGuild(long guildId, ref List<GuildRecruitEntity> recruitList)
        {
            var now = K2Common.GetDateTime();
            if (_guildJoinByGuildIdDic.TryGetValue(guildId, out ConcurrentDictionary<long, GuildRecruitEntity> recruitDic) == true)
            {
                foreach (var entity in recruitDic.Values)
                {
                    if (entity.IsValidPeriod(now) == true)
                    {
                        continue;
                    }

                    var clone = entity.Clone();
                    clone.Delete();
                    recruitList.Add(clone);
                }
            }
        }

        public void GuildLoad(eGuildLoadType type, long ownerId, GuildInfoData guildInfo, List<GuildRecruitEntity> recruitList)
        {
            GuildCommunityModel guildModel = null;
            if (guildInfo.guild.IsVaild() == true)
            {
                if (_guildDic.TryGetValue(guildInfo.guild.id, out guildModel) == false)
                {
                    //var guildModel = PoolManager.Instance.GetObject<GuildCommunityModel>();
                    //guildModel.Init(guildInfo.guild, guildInfo.memberList);
                    //AddGuild(guildModel);
                    guildModel = AddGuild(guildInfo, ownerId);
                    //guildModel.AddGuildRelationList(relationList);

                    foreach (var info in recruitList)
                    {
                        AddGuildRecruit(info);
                    }

                    // todo - 길드 가입상태인데 recruitEntity 가 존재한다면 정리 작업 필요할까??
                    //if (type == eGuildInfoSelectType.CharacterLogin)
                    //    ClearGuildRecruit(loadCharId);
                }
            }
            else if (ownerId > 0)
            {
                // 길드를 한번이라도 가입한 경우가 있는 경우라면 guildId를 0으로 설정 (길드 재가입 시 쿨타임 적용때문에)
                _guildByCharIdDic[ownerId] = 0;

                foreach (var info in recruitList)
                {
                    AddGuildRecruit(info);
                }
            }

            switch (type)
            {
                case eGuildLoadType.CharacterLogin:
                    {
                        if (_guildByCharIdDic.ContainsKey(ownerId) == true)
                        {
                            var cmd = new ChattingCommunityInfoCmd();
                            cmd.type = eUpdateCommunityType.CommunityUpdate;
                            cmd.charId = ownerId;
                            CommunityWorkerManager.Instance.AddCommand(cmd);

                            // 캐릭터 로그인에 한해서만 연관 길드정보를 로드(아니면 관계가 맺어진 모든 길드들이 로드됨)
                            ReqRelationGuildInfoQuery(guildModel);
                        }
                        else
                        {
                            SLogManager.Instance.ErrorLog($"not exist _guildByCharIdDic - charId:{ownerId}");
                        }
                    }
                    break;

                case eGuildLoadType.GuildRelation:
                    {
                        _delayedRelationLoadEvent.RunAfterDeleyedEvent(ownerId, guildInfo.guild.id);
                    }
                    break;
            }
        }

        private void ReqRelationGuildInfoQuery(GuildCommunityModel guildModel)
        {
            if (guildModel == null)
            {
                return;
            }

            bool hasRelation = false;
            var relationList = guildModel.GetGuildRelationList();
            foreach (var relationEntity in relationList)
            {
                var guildId = relationEntity.targetId;

                if (_guildDic.ContainsKey(guildId) == false)
                {
                    _delayedRelationLoadEvent.AddEvent(guildModel.guildId, guildId);
                    SendZMQGuildInfoQuery(eGuildLoadType.GuildRelation, guildModel.guildId, guildId);
                    hasRelation = true;
                }
            }

            if (hasRelation)
            {
                _notCompleteLoadGuildIdSet.Add(guildModel.guildId);
            }
        }

        private GuildCommunityModel AddGuild(GuildInfoData guildInfo, long reqCharId, bool isCreate = false)
        {
            var guildModel = PoolManager.Instance.GetObject<GuildCommunityModel>();
            guildModel.Init(guildInfo);

            _guildDic.TryAdd(guildModel.guildId, guildModel);
            _guildByNameDic.TryAdd(guildModel.guildName, guildModel);

            foreach (var member in guildModel.GetGuildMemberList())
            {
                if (isCreate == false &&
                    _guildByCharIdDic.ContainsKey(member.charId) == true &&
                    _guildByCharIdDic[member.charId] != member.guildId)
                {
                    // 해당 상황이 절대 나와선 안됨
                    SLogManager.Instance.ErrorLog($"diff _guildByCharIdDic's guildId - charId:{member.charId}, guildId:{member.guildId}/{_guildByCharIdDic[member.charId]}");
                }

                _guildByCharIdDic[member.charId] = member.guildId;

                if (reqCharId == member.charId)
                {
                    var cmd = new ChattingCommunityInfoCmd();
                    cmd.type = eUpdateCommunityType.CommunityUpdate;
                    cmd.charId = reqCharId;
                    CommunityWorkerManager.Instance.AddCommand(cmd);
                }
            }

            return guildModel;
        }

        private void RemoveGuild(GuildInfoData guildInfo)
        {
            if (true == _guildDic.TryGetValue(guildInfo.guild.id, out GuildCommunityModel guildModel))
            {
                _guildByNameDic.TryRemove(guildInfo.guild.name, out var _);
                _guildDic.TryRemove(guildInfo.guild.id, out var _);

                foreach (var member in guildModel.GetGuildMemberList())
                {
                    GuildRankingUpdate(member.charId, guildInfo.guild, true);

                    // 길드를 한번이라도 가입한 경우가 있는 경우라면 guildId를 0으로 설정 (길드 재가입 시 쿨타임 적용때문에)
                    _guildByCharIdDic[member.charId] = 0;
                    var chatCmd = new ChattingCommunityInfoCmd();
                    chatCmd.type = eUpdateCommunityType.CommunityUpdate;
                    chatCmd.charId = member.charId;
                    CommunityWorkerManager.Instance.AddCommand(chatCmd);
                }

                foreach (var relation in guildModel.GetGuildRelationList())
                {
                    guildModel.RemoveRelationEntity(relation);
                }

                foreach (var enermy in guildModel.GetGuildEnermyList())
                {
                    guildModel.RemoveRelationEntity(enermy);
                }

                guildModel.RemovePlace();

                guildModel.Dispose();

                var guildDeleteCmd = new RankingGuildDeleteCmd();
                guildDeleteCmd.guildId = guildInfo.guild.id;
                RankingCommunityManager.Instance.AddCommand(guildDeleteCmd);
            }
        }

        private bool AddGuildMember(long guildId, List<GuildMemberEntity> memberList, eGuildInfoSyncSaveType type, out bool isFull)
        {
            isFull = false;
            if (memberList == null)
            {
                SLogManager.Instance.ErrorLog($"memberList is null type:{type}");
                return false;
            }

            if (_guildDic.TryGetValue(guildId, out var guildModel) == true)
            {
                foreach (var member in memberList)
                {
                    if (member.IsVaild() == false)
                    {
                        SLogManager.Instance.ErrorLog($"invalid member guildId:{member.guildId}, charId:{member.charId}, type:{type}");
                        continue;
                    }
                    else if (guildModel.guildId != member.guildId)
                    {
                        SLogManager.Instance.ErrorLog($"diff member guildId:{guildModel.guildId}/{member.guildId}, charId:{member.charId}, type:{type}");
                        continue;
                    }

                    guildModel.AddMember(member);
                    _guildByCharIdDic[member.charId] = member.guildId;

                    {
                        var cmd = new ChattingCommunityInfoCmd();
                        cmd.type = eUpdateCommunityType.CommunityUpdate;
                        cmd.charId = member.charId;
                        CommunityWorkerManager.Instance.AddCommand(cmd);
                    }

                    SendZMQGuildNotify(eGuildNotifyType.MemberJoin, member.guildId, member.charId, member.charName, guildModel.guildName);

                    SLogManager.Instance.InfoLog("AddGuildMember() charId:{0}, guildId:{1}, type:{2}", member.charId, member.guildId, type);

                    isFull = guildModel.IsFull();
                }
            }
            else
            {
                SLogManager.Instance.ErrorLog($"not found guildModel guildId:{guildId}");
                return false;
            }

            return true;
        }

        private bool RemoveGuildMember(List<GuildMemberEntity> memberList, long requesterCharId, eGuildInfoSyncSaveType type)
        {
            if (memberList == null)
            {
                SLogManager.Instance.ErrorLog($"memberList is null type:{type}");
                return false;
            }

            var result = true;
            foreach (var member in memberList)
            {
                if (_guildDic.TryGetValue(member.guildId, out GuildCommunityModel guildModel) == true)
                {
                    guildModel.RemoveMember(member.charId);

                    // 길드를 한번이라도 가입한 경우가 있는 경우라면 guildId를 0으로 설정 (길드 재가입 시 쿨타임 적용때문에)
                    _guildByCharIdDic[member.charId] = 0;

                    GuildRankingUpdate(member.charId, guildModel.GetGuildInfo(), false);

                    {
                        var chatCmd = new ChattingCommunityInfoCmd();
                        chatCmd.type = eUpdateCommunityType.CommunityUpdate;
                        chatCmd.charId = member.charId;
                        CommunityWorkerManager.Instance.AddCommand(chatCmd);
                    }
                    SendZMQGuildNotify(type == eGuildInfoSyncSaveType.Leave ? eGuildNotifyType.MemberLeave : eGuildNotifyType.MemberBanish, member.guildId, member.charId, member.charName, string.Empty);

                    SLogManager.Instance.InfoLog("RemoveGuildMember() requesterCharId:{0}, targetCharId:{1}, guildId:{2}, type:{3}", requesterCharId, member.charId, member.guildId, type);
                }
                else
                {
                    result = false;
                    SLogManager.Instance.ErrorLog($"not found guildModel requesterCharId:{requesterCharId}, targetCharId:{member.charId}, guildId:{member.guildId}, type:{type}");
                }
            }

            return result;
        }

        private void AddGuildRecruit(GuildRecruitEntity info)
        {
            var entity = PoolManager.Instance.GetObject<GuildRecruitEntity>();
            entity.Copy(info);

            if (_guildJoinByCharIdDic.TryGetValue(entity.charId, out var _) == false)
            {
                _guildJoinByCharIdDic.TryAdd(entity.charId, new ConcurrentDictionary<long, GuildRecruitEntity>());
            }

            if (_guildJoinByCharIdDic[entity.charId].TryGetValue(entity.guildId, out var _) == false)
            {
                _guildJoinByCharIdDic[entity.charId].TryAdd(entity.guildId, entity);
            }
            else
            {
                SLogManager.Instance.ErrorLog($"duplicate _guildJoinByCharIdDic charId:{entity.charId}, guildId:{entity.guildId}");
            }

            if (_guildJoinByGuildIdDic.TryGetValue(entity.guildId, out var _) == false)
            {
                _guildJoinByGuildIdDic.TryAdd(entity.guildId, new ConcurrentDictionary<long, GuildRecruitEntity>());
            }

            if (_guildJoinByGuildIdDic[entity.guildId].TryGetValue(entity.charId, out var _) == false)
            {
                _guildJoinByGuildIdDic[entity.guildId].TryAdd(entity.charId, entity);
            }
            else
            {
                SLogManager.Instance.ErrorLog($"duplicate _guildJoinByGuildIdDic charId:{entity.charId}, guildId:{entity.guildId}");
            }

            SLogManager.Instance.InfoLog("AddGuildRecruit() charId:{0}, guildId:{1}", entity.charId, entity.guildId);

        }

        private void RemoveGuildRecruit(GuildRecruitEntity info)
        {
            if (_guildJoinByCharIdDic.TryGetValue(info.charId, out var recruitDic) == true &&
                recruitDic.TryGetValue(info.guildId, out var entity) == true)
            {
                _guildJoinByCharIdDic[info.charId].TryRemove(info.guildId, out var _);
                _guildJoinByGuildIdDic[info.guildId].TryRemove(info.charId, out var _);

                SLogManager.Instance.InfoLog("RemoveGuildRecruit() charId:{0}, guildId:{1}", entity.charId, entity.guildId);
                entity.Dispose();
            }
            else
            {
                SLogManager.Instance.ErrorLog($"not found recruit - charId:{info.charId}, guildId:{info.guildId}");
            }

        }

        private void GuildRecruitUpdate(List<GuildRecruitEntity> recruitList)
        {
            foreach (var entity in recruitList)
            {
                if (entity.GetDBEntityState().IsEntityDeleted() == true)
                {
                    RemoveGuildRecruit(entity);
                }
                else
                {
                    AddGuildRecruit(entity);
                }
            }
        }

        public void GuildRankingUpdate(long charId, GuildEntity guild, bool isRemove)
        {
            if (guild == null)
                return;

            var cmd = new RankingGuildUpdateCmd();
            cmd.charId = charId;
            cmd.guildId = guild.id;
            cmd.crestId = guild.crestId;
            cmd.guildName = guild.name;
            cmd.isRemove = isRemove;
            RankingCommunityManager.Instance.AddCommand(cmd);
        }

        public void GuildInfoSync(ePacketCommonResult result, eGuildInfoSyncSaveType type, GuildRequesterData requesterInfo, GuildInfoData guildInfo, List<GuildRecruitEntity> recruitList)
        {
            switch (type)
            {
                case eGuildInfoSyncSaveType.Create:
                    GuildCreateResult(result, requesterInfo, guildInfo, recruitList);
                    break;
                case eGuildInfoSyncSaveType.Dissolve:
                    GuildDissolutionResult(result, requesterInfo, guildInfo, recruitList);
                    break;
                case eGuildInfoSyncSaveType.JoinRequest:
                case eGuildInfoSyncSaveType.JoinReject:
                case eGuildInfoSyncSaveType.JoinCancel:
                    GuildMemberAddResult(result, type, requesterInfo, null, recruitList);
                    break;
                case eGuildInfoSyncSaveType.JoinDirect:
                case eGuildInfoSyncSaveType.JoinAccept:
                    GuildMemberAddResult(result, type, requesterInfo, guildInfo, recruitList);
                    break;
                case eGuildInfoSyncSaveType.Banish:
                case eGuildInfoSyncSaveType.Leave:
                    GuildMemberRemoveResult(result, type, requesterInfo, guildInfo, recruitList);
                    break;
                case eGuildInfoSyncSaveType.RecruitClearByGuild:
                case eGuildInfoSyncSaveType.RejoinCoolTimeRemove:
                case eGuildInfoSyncSaveType.InvalidRequestClearByChar:
                case eGuildInfoSyncSaveType.InvalidRequestClearByGuild:
                    GuildRecruitUpdateResult(result, type, requesterInfo, recruitList);
                    break;
                case eGuildInfoSyncSaveType.Ranking:
                    GuildRankingSync(guildInfo.guild);
                    break;
            }
        }

        private void GuildCreateResult(ePacketCommonResult result, GuildRequesterData requesterInfo, GuildInfoData guildInfo, List<GuildRecruitEntity> recruitList)
        {
            GuildInfoData resultGuildInfo = null;
            if (result == ePacketCommonResult.Success)
            {
                GuildRecruitUpdate(recruitList);

                if (guildInfo.guild.IsVaild() == false)
                {
                    // 길드를 생성 하였는데 전달된 데이타가 invalid .. (나오면 안되는 케이스)
                    SLogManager.Instance.ErrorLog($"invalid guild data - charId:{requesterInfo.charId}, guildName:{guildInfo.guild.name}");
                    result = ePacketCommonResult.InternalError;
                }
                else if (_guildDic.ContainsKey(guildInfo.guild.id) == true)
                {
                    // 길드를 생성 하였는데 이미 _guildDic 에 존재...  (나오면 안되는 케이스)
                    SLogManager.Instance.ErrorLog($"Already exist guildModel - charId:{requesterInfo.charId}, guildName:{guildInfo.guild.name}");
                    result = ePacketCommonResult.InternalError;
                }
                else
                {
                    AddGuild(guildInfo, requesterInfo.charId, true);

                    GuildRankingUpdate(requesterInfo.charId, guildInfo.guild, false);

                    resultGuildInfo = guildInfo;

                    SLogManager.Instance.InfoLog("GuildCreate guildId:{0}, guildName:{1}, result:{2}", guildInfo.guild.id, guildInfo.guild.name, result);
                }
            }
            else
            {
                SLogManager.Instance.ErrorLog($"fail guildCreate guildId:{guildInfo.guild.id}, guildName:{guildInfo.guild.name}, guildState:{guildInfo.guild.state}, result:{result}");
            }

            SendGuildCreate(result, requesterInfo, resultGuildInfo);
        }

        private void GuildDissolutionResult(ePacketCommonResult result, GuildRequesterData requesterInfo, GuildInfoData guildInfo, List<GuildRecruitEntity> recruitList)
        {
            if (result == ePacketCommonResult.Success)
            {
                GuildRecruitUpdate(recruitList);
                if (guildInfo.guild.IsDissolved() ||
                    guildInfo.guild.IsDeleted())
                {

                    RemoveGuild(guildInfo);

                    SLogManager.Instance.InfoLog("GuildDissolution guildId:{0}, guildName:{1}", guildInfo.guild.id, guildInfo.guild.name);
                }
                else
                {
                    result = ePacketCommonResult.InternalError;
                    SLogManager.Instance.ErrorLog($"Invalid guild state guildId:{guildInfo.guild.id}, guildName:{guildInfo.guild.name}, guildState:{guildInfo.guild.state}");
                }
            }

            SendGuildActionResult(result, eGuildActionType.Dissolve, requesterInfo, guildInfo);
        }

        private void GuildMemberAddResult(ePacketCommonResult result, eGuildInfoSyncSaveType type, GuildRequesterData requesterInfo, GuildInfoData guildInfo, List<GuildRecruitEntity> recruitList)
        {
            bool isFull = false;
            long guildId = 0;
            if (result == ePacketCommonResult.Success)
            {
                GuildRecruitUpdate(recruitList);

                if (guildInfo != null)
                {
                    if (guildInfo.guild.IsVaild() == true)
                    {
                        guildId = guildInfo.guild.id;
                    }
                    else if (guildInfo.memberList.Count > 0)
                    {
                        guildId = guildInfo.memberList[0].guildId;
                    }
                    else
                    {
                        guildId = 0;
                    }

                    if (false == AddGuildMember(guildId, guildInfo.memberList, type, out isFull))
                    {
                        result = ePacketCommonResult.InternalError;
                    }
                    else
                    {
                        var guildModel = GetGuildModelByGuildId(guildId);
                        var entity = guildModel?.GetGuildInfoData();
                        if (entity != null)
                            guildInfo = entity;

                        GuildRankingUpdate(requesterInfo.charId, guildInfo.guild, false);
                    }
                }
            }
            else
            {
                requesterInfo.GetParamValue(eGuildParamType.TargetCharId, out long targetCharId);
                SLogManager.Instance.ErrorLog($"fail guild join requestCharId:{requesterInfo.charId}, targetCharId:{targetCharId}, type:{type}, result:{result}");
            }

            //var guildModel = this.GetGuildModelByGuildId(guildId);
            //resultGuildEntity;

            switch (type)
            {
                case eGuildInfoSyncSaveType.JoinDirect:
                case eGuildInfoSyncSaveType.JoinRequest:
                case eGuildInfoSyncSaveType.JoinCancel:
                    SendZMQGuildJoinResult(result, requesterInfo, guildInfo);
                    break;
                case eGuildInfoSyncSaveType.JoinAccept:
                    SendGuildActionResult(result, eGuildActionType.JoinAccept, requesterInfo, guildInfo);
                    if (isFull == true)
                    {
                        SendZMQGuildInfoSyncSave(eGuildInfoSyncSaveType.RecruitClearByGuild, guildId, requesterInfo, null, null, null, null);
                    }

                    break;
                case eGuildInfoSyncSaveType.JoinReject:
                    SendGuildActionResult(result, eGuildActionType.JoinReject, requesterInfo, guildInfo);
                    break;
            }
        }

        private void GuildMemberRemoveResult(ePacketCommonResult result, eGuildInfoSyncSaveType type, GuildRequesterData requesterInfo, GuildInfoData guildInfo, List<GuildRecruitEntity> recruitList)
        {
            if (result == ePacketCommonResult.Success)
            {
                GuildRecruitUpdate(recruitList);

                if (guildInfo != null)
                {
                    if (false == RemoveGuildMember(guildInfo.memberList, requesterInfo.charId, type))
                    {
                        result = ePacketCommonResult.InternalError;
                    }
                    else
                    {
                        GuildRankingUpdate(requesterInfo.charId, guildInfo.guild, false);
                    }
                }
            }
            else
            {
                requesterInfo.GetParamValue(eGuildParamType.TargetCharId, out long targetCharId);
                SLogManager.Instance.ErrorLog($"fail banish requestCharId:{requesterInfo.charId}, targetCharId:{targetCharId}, type:{type}, result:{result}");
            }

            switch (type)
            {
                case eGuildInfoSyncSaveType.Banish:
                    SendGuildActionResult(result, eGuildActionType.Banish, requesterInfo, guildInfo);
                    break;
                case eGuildInfoSyncSaveType.Leave:
                    SendGuildActionResult(result, eGuildActionType.Leave, requesterInfo, guildInfo);
                    break;
            }
        }

        private void GuildRecruitUpdateResult(ePacketCommonResult result, eGuildInfoSyncSaveType type, GuildRequesterData requesterInfo, List<GuildRecruitEntity> recruitList)
        {
            if (result == ePacketCommonResult.Success)
            {
                GuildRecruitUpdate(recruitList);
            }
            else
            {
                SLogManager.Instance.ErrorLog($"fail recruit update requestCharId:{requesterInfo.charId}, type:{type}, result:{result}");
            }

            switch (type)
            {
                case eGuildInfoSyncSaveType.RejoinCoolTimeRemove:
                    SendZMQGuildRejoinCoolTimeActionResult(ePacketCommonResult.Success, requesterInfo, eGuildRejoinCoolTimeActionType.Remove);
                    break;
            }
        }

        private void GuildRankingSync(GuildEntity guild)
        {
            var guildModel = GetGuildModelByGuildId(guild.id);
            guildModel?.SetRankingNo(guild.ranking);
        }

        private void SendGuildCreate(ePacketCommonResult result, GuildRequesterData requesterInfo, GuildInfoData guildInfo)
        {
            var packet = new SC_ZMQ_GuildCreate();
            packet.result = result;
            packet.charId = requesterInfo.charId;

            if (guildInfo != null)
                packet.guildInfo.Copy(guildInfo);

            //var myServerGroupId = ServerModule.Instance.GetMyServerGroupId();
            if (result == ePacketCommonResult.Success)
            {
                //ServerModule.Instance.GetServerController().BroadcastZMQ(eServerType.Game, myServerGroupId, packet);
                BroadcastZMQToGameNode(packet);
            }
            else
            {
                ServerModule.Instance.Controller.SendZMQ(eServerType.Game, packet, eServerType.Community, requesterInfo.sourceServerId);
                //ServerModule.Instance.GetServerController().SendZMQ(string.Empty, 0, myServerGroupId, requesterInfo.sourceServerId, packet);
            }
        }

        private void SendZMQGuildJoinResult(ePacketCommonResult result, GuildRequesterData requesterInfo, GuildInfoData guildInfo)
        {
            var packet = new SC_ZMQ_GuildJoin();
            packet.result = result;
            packet.requesterInfo.Copy(requesterInfo);

            if(guildInfo != null)
                packet.guildInfo.Copy(guildInfo);

            if (result == ePacketCommonResult.Success)
            {
                //var myServerGroupId = ServerModule.Instance.GetMyServerGroupId();
                //ServerModule.Instance.GetServerController().BroadcastZMQ(eServerType.Game, myServerGroupId, packet);
                BroadcastZMQToGameNode(packet);
            }
            else
            {
                ServerModule.Instance.Controller.SendZMQ(eServerType.Game, packet, eServerType.Community, requesterInfo.sourceServerId);
            }
        }

        private void SendZMQGuildRejoinCoolTimeActionResult(ePacketCommonResult result, GuildRequesterData requesterInfo, eGuildRejoinCoolTimeActionType actionType)
        {
            var packet = new SC_ZMQ_GuildRejoinCoolTimeAction();
            packet.result = result;
            packet.requesterInfo.Copy(requesterInfo);
            packet.actionType = actionType;
            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, packet, eServerType.Community, requesterInfo.sourceServerId);
        }

        private void SendZMQGuildActionResultByDissolvedRelation(ePacketCommonResult result, eGuildActionType actionType, GuildRequesterData requesterInfo, List<GuildRelationEntity> relationList)
        {
            foreach (var guildRelation in relationList)
            {
                var guildInfo = new GuildInfoData();
                guildInfo.guild.id = guildRelation.guildId;
                guildInfo.relationList.Add(guildRelation);

                SendGuildActionResult(result, actionType, requesterInfo, guildInfo);
            }
        }

        private void SendZMQGuildActionResultByRelation(ePacketCommonResult result, eGuildActionType actionType, GuildRequesterData requesterInfo, List<GuildRelationEntity> relationList)
        {
            var guildInfo = new GuildInfoData();

            if (relationList != null)
            {
                guildInfo.relationList.AddRange(relationList);
            }

            SendGuildActionResult(result, actionType, requesterInfo, guildInfo);
        }

        private void SendZMQGuildActionResultByCrest(ePacketCommonResult result, eGuildActionType actionType, GuildRequesterData requesterInfo, GuildEntity guildEntity, List<GuildCrestEntity> crestList)
        {
            var guildInfo = new GuildInfoData();

            if (guildEntity != null)
            {
                guildInfo.guild.Copy(guildEntity);
            }

            if (crestList != null)
            {
                guildInfo.collectedCrestList.AddRange(crestList);
            }

            SendGuildActionResult(result, actionType, requesterInfo, guildInfo);
        }

        private void SendGuildActionResult(ePacketCommonResult result, eGuildActionType actionType, GuildRequesterData requesterInfo, GuildEntity guildEntity, GuildMemberEntity memberEntity)
        {
            var guildInfo = new GuildInfoData();

            if (guildEntity != null)
            {
                guildInfo.guild.Copy(guildEntity);
            }

            if (memberEntity != null)
            {
                guildInfo.memberList.Add(memberEntity.Clone());
            }

            SendGuildActionResult(result, actionType, requesterInfo, guildInfo);
        }

        private void SendGuildActionResult(ePacketCommonResult result, eGuildActionType actionType, GuildRequesterData requesterInfo, GuildInfoData guildInfo)
        {
            var packet = new SC_ZMQ_GuildAction();
            packet.result = result;

            if (guildInfo != null)
            {
                packet.guildInfo.Copy(guildInfo);
            }

            packet.type = actionType;
            packet.requesterInfo.Copy(requesterInfo);

            if (result == ePacketCommonResult.Success)
            {
                BroadcastZMQToGameNode(packet);
            }
            else
            {
                ServerModule.Instance.Controller.SendZMQ(eServerType.Game, packet, eServerType.Community, requesterInfo.sourceServerId);
            }
        }

        public long GetMyGuildId(long charId)
        {
            var guildModel = GetGuildModel(charId);

            return guildModel != null ? guildModel.guildId : 0;
        }

        public string GetMyGuildName(long charId)
        {
            var guildModel = GetGuildModel(charId);

            return guildModel != null ? guildModel.guildName : "";
        }

        public List<long> GetGuildMemberList(long guildId)
        {
            if (_guildDic.TryGetValue(guildId, out var guildModel) == false)
                return null;

            return guildModel.GetGuildMemberList().Select(x => x.charId).ToList();
        }

        public ePacketCommonResult ReqGuildMemberInfo(GuildRequesterData requesterInfo)
        {
            var result = ePacketCommonResult.Success;
            var guildModel = GetGuildModel(requesterInfo.charId);
            if (guildModel == null)
            {
                result = ePacketCommonResult.NotGuildJoined;
            }

            SendZMQGuildMemberInfoResult(result, requesterInfo, guildModel);

            return result;
        }

        public void SendZMQGuildMemberInfoResult(ePacketCommonResult result, GuildRequesterData requesterInfo, GuildCommunityModel guildModel)
        {
            var packet = new SC_ZMQ_GuildMemberInfo();
            packet.result = result;
            if (requesterInfo != null)
            {
                packet.requesterInfo.Copy(requesterInfo);
            }

            if (guildModel != null)
            {
                foreach (var memberEntity in guildModel.GetGuildMemberList())
                {
                    var clone = memberEntity.CopyTo();
                    var findBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(clone.charId);
                    clone.SetLoginInfo(findBBModel != null);

                    packet.guildMemberList.Add(clone);
                }
            }

            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, packet, eServerType.Community, requesterInfo.sourceServerId);
        }

        public ePacketCommonResult ReqGuildRecruitInfo(GuildRequesterData requesterInfo)
        {
            var result = ePacketCommonResult.Success;
            long guildId = 0;
            var guildModel = GetGuildModel(requesterInfo.charId);
            if (guildModel == null)
            {
                result = ePacketCommonResult.NotGuildJoined;
            }
            else
            {
                var memberEntity = guildModel.GetGuildMemberInfo(requesterInfo.charId);
                if (memberEntity == null)
                {
                    result = ePacketCommonResult.NotGuildJoined;
                }
                else if (memberEntity.grade != (byte)eGuildMemberGrade.Master &&
                    memberEntity.grade != (byte)eGuildMemberGrade.ViceMaster)
                {
                    result = ePacketCommonResult.NoPermissionGuildAction;
                }
                else
                {
                    guildId = guildModel.guildId;
                }
            }

            SendZMQGuildRecruitInfoResult(result, requesterInfo, guildId);

            return result;
        }

        public ePacketCommonResult GuildRelationInfo(GuildRequesterData requesterInfo)
        {
            var result = ePacketCommonResult.Success;
            var packet = new SC_ZMQ_GuildRelationInfo();

            do
            {
                var guildModel = GetGuildModel(requesterInfo.charId);
                if (guildModel == null)
                {
                    result = ePacketCommonResult.NotGuildJoined;
                    break;
                }

                var enermyList = guildModel.GetGuildEnermyList();
                foreach (var enermy in enermyList)
                {
                    var userBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(enermy.targetId);
                    if (userBBModel != null)
                    {
                        packet.onlineEnermyList.Add(userBBModel.Extraction());
                    }

                    packet.guildEnermyList.Add(enermy.CopyTo());
                }

                // 커뮤니티노드에서 guildEnermyList에 relationType이 eGuildRelationType.Enermy인 아이들만 넣어주고, 게임노드에서
                // eGuildRelationType.Hostile를 넣어주고 있던 것을 커뮤니티에서 길드 문장 갱신 후 넣어주도록 변경

                var relationList = guildModel.GetGuildRelationList();

                foreach (var relation in relationList)
                {
                    var relationGuildModel = GetGuildModelByGuildId(relation.targetId);
                    if (relationGuildModel != null)
                    {
                        if (relation.targetCrestClassId != relationGuildModel.GetGuildInfo().crestId)
                        {
                            relation.targetCrestClassId = relationGuildModel.GetGuildInfo().crestId;
                            DbEntityManager.UpdateEntity(relation);
                        }
                        packet.guildEnermyList.Add(relation.CopyTo());
                    }
                }
            }
            while (false);

            packet.result = result;
            packet.requesterInfo.Copy(requesterInfo);
            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, packet, eServerType.Community, requesterInfo.sourceServerId);

            return result;
        }

        public ePacketCommonResult GuildInviteReq(string targetName, long guildId, long requesterId)
        {
            var result = ePacketCommonResult.Success;
            do
            {
                var targetBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(targetName);
                if (targetBBModel == null)
                {
                    result = ePacketCommonResult.NotFoundTargetToInviteGuild;
                    break;
                }

                if (_guildDic.TryGetValue(guildId, out var guildModel) == false)
                {
                    result = ePacketCommonResult.NotFoundGuild;
                    break;
                }

                if (guildModel.IsFull())
                {
                    result = ePacketCommonResult.GuildMemberFull;
                    break;
                }

                var targetCharId = targetBBModel.userInfoData.charId;

                if (_guildByCharIdDic.TryGetValue(targetCharId, out var targetGuildId) && targetGuildId > 0)
                {
                    result = ePacketCommonResult.AlreadyOtherGuildJoined;
                    break;
                }

                if (guildModel.IsReserveJoinUser(targetCharId))
                {
                    result = ePacketCommonResult.AlreadyReserveJoinGuild;
                    break;
                }

                var targetEntity = guildModel.GetGuildMemberInfo(targetCharId);
                if (targetEntity != null && targetEntity.IsVaild())
                {
                    result = ePacketCommonResult.AlreadyGuildJoined;
                    break;
                }

                var requesterEntity = guildModel.GetGuildMemberInfo(requesterId);
                if (requesterEntity == null || requesterEntity.IsVaild() == false)
                {
                    result = ePacketCommonResult.NotBelongToGuildMember;
                    break;
                }

                var now = K2Common.GetDateTime();
                if (requesterEntity.RegisterInviteExpireTimer(targetBBModel.userInfoData.name, now) == false)
                {
                    result = ePacketCommonResult.AlreadyInviteGuild;
                    break;
                }

                var sendPacket = new SC_ZMQ_GuildInviteNotify();
                sendPacket.guildId = guildId;
                sendPacket.guildName = guildModel.guildName;
                sendPacket.reqCharId = requesterId;
                sendPacket.reqCharName = requesterEntity.charName;
                sendPacket.targetUid = targetBBModel.userInfoData.uId;
                ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, targetBBModel.userInfoData.serverId);

            } while (false);

            return result;
        }

        public ePacketCommonResult GuildInviteAccept(long acceptCharId, long reqCharId, bool isAccept)
        {
            var result = ePacketCommonResult.Success;
            do
            {
                if (_guildByCharIdDic.TryGetValue(reqCharId, out var guildId) == false)
                {
                    result = ePacketCommonResult.NotGuildMemberReq;
                    break;
                }

                if (_guildDic.TryGetValue(guildId, out var guildModel) == false)
                {
                    result = ePacketCommonResult.NotFoundGuild;
                    break;
                }

                if (guildModel.IsFull())
                {
                    result = ePacketCommonResult.GuildMemberFull;
                    break;
                }

                if (guildModel.IsReserveJoinUser(acceptCharId))
                {
                    result = ePacketCommonResult.AlreadyReserveJoinGuild;
                    break;
                }

                if (_guildByCharIdDic.TryGetValue(acceptCharId, out var acceptGuildId) && acceptGuildId > 0)
                {
                    result = ePacketCommonResult.AlreadyOtherGuildJoined;
                    break;
                }

                var acceptBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(acceptCharId);
                if (acceptBBModel == null)
                {
                    result = ePacketCommonResult.NotFoundTargetToInviteGuild;
                    break;
                }

                var requestBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(reqCharId);
                if (requestBBModel == null)
                {
                    result = ePacketCommonResult.NotFoundRequesterToInviteGuild;
                    break;
                }

                var requesterEntity = guildModel.GetGuildMemberInfo(reqCharId);
                if (requesterEntity == null || requesterEntity.IsVaild() == false)
                {
                    result = ePacketCommonResult.NotBelongToGuildMember;
                    break;
                }

                if (requesterEntity.RemoveInviteExpireTimer(acceptBBModel.userInfoData.name) == false)
                {
                    result = ePacketCommonResult.NotInvitedGuild;
                    break;
                }

                if (isAccept)
                {
                    GuildRequesterData requesterInfo = new GuildRequesterData();
                    requesterInfo.paramDic.Add(eGuildParamType.GuildId, new GuildParam(eGuildParamType.GuildId, guildId));
                    requesterInfo.paramDic.Add(eGuildParamType.Password, new GuildParam(eGuildParamType.Password, guildModel.password));

                    var memberEntity = new GuildMemberEntity();
                    memberEntity.Init(acceptBBModel.userInfoData.charId, acceptBBModel.userInfoData.name, (int)acceptBBModel.userInfoData.charClassType, guildModel.guildId, 5);
                    SendZMQGuildInfoSyncSave(eGuildInfoSyncSaveType.JoinDirect, guildId, requesterInfo, null, memberEntity, null, null);
                }

                // 요청자에게 노티
                var sendPacket = new SC_ZMQ_GuildInviteResultNotify();
                sendPacket.result = ePacketCommonResult.Success;
                sendPacket.requesterUid = requestBBModel.userInfoData.uId;
                sendPacket.targetCharNameList = new List<string> { acceptBBModel.userInfoData.name };
                sendPacket.isAccept = isAccept;
                ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, requestBBModel.userInfoData.serverId);

            } while (false);

            return result;
        }

        public void SendZMQGuildRecruitInfoResult(ePacketCommonResult result, GuildRequesterData requesterInfo, long guildId)
        {
            var packet = new SC_ZMQ_GuildRecruitInfo();
            packet.result = result;
            if (requesterInfo != null)
                packet.requesterInfo.Copy(requesterInfo);

            var isExistInvalidRequest = false;
            if (guildId > 0)
            {
                if (_guildJoinByGuildIdDic.TryGetValue(guildId, out var recruitDic) == true)
                {
                    var now = K2Common.GetDateTime();
                    foreach (var entity in recruitDic.Values)
                    {
                        if (entity.IsValidPeriod(now) == false)
                        {
                            isExistInvalidRequest = true;
                            continue;
                        }

                        var clone = entity.CopyTo();
                        packet.guildRecruitList.Add(clone);
                    }
                }
            }

            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, packet, eServerType.Community, requesterInfo.sourceServerId);

            if (isExistInvalidRequest == true)
            {
                SendZMQGuildInfoSyncSave(eGuildInfoSyncSaveType.InvalidRequestClearByGuild, guildId, requesterInfo, null, null, null, null);
            }
        }

        #region guildSearch
        public ePacketCommonResult ReqGuildSearch(eGuildSearchType type, GuildRequesterData requesterInfo)
        {
            var result = ePacketCommonResult.Success;
            var guildModel = GetGuildModel(requesterInfo.charId);
            if (guildModel != null)
            {
                result = ePacketCommonResult.NotGuildJoined;
                SendGuildSearchResult(result, type, requesterInfo, null);
                return result;
            }

            switch (type)
            {
                case eGuildSearchType.Ranking:
                    SendGuildSearchResultByRanking(requesterInfo);
                    break;
                case eGuildSearchType.Name:
                    DbZMQGuildSearchQuery(type, null, requesterInfo);
                    break;
                case eGuildSearchType.JoinRequest:
                    SendGuildSearchResultByJoinRequest(requesterInfo);
                    break;
            }

            return result;
        }

        private void SendGuildSearchResult(ePacketCommonResult result, eGuildSearchType type, GuildRequesterData requesterInfo, List<GuildSearchInfoPacket> infoList)
        {
            var packet = new SC_ZMQ_GuildSearch();
            packet.result = result;
            packet.type = type;

            if (requesterInfo != null)
            {
                packet.requesterInfo.Copy(requesterInfo);
            }

            if (infoList != null)
            {
                packet.searchGuildList.AddRange(infoList);
            }

            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, packet, eServerType.Community, requesterInfo.sourceServerId);
        }

        private void SendGuildSearchResultByRanking(GuildRequesterData requesterInfo)
        {
            var searchGuildList = new List<GuildSearchInfoPacket>();

            _guildJoinByCharIdDic.TryGetValue(requesterInfo.charId, out var recruitDic);

            int count = 0;
            foreach (var guild in _guildDic.Values)
            {
                var guildInfo = guild.GetGuildInfo();
                if (guildInfo.state != (byte)eGuildState.Normal)
                    continue;

                if (guildInfo.GetJoinType() != eGuildJoinType.AutoJoin &&
                    guildInfo.GetJoinType() != eGuildJoinType.NeedAccept)
                {
                    continue;
                }

                var searchGuildClone = guildInfo.GetSearchInfoClone();

                if (recruitDic != null && recruitDic.ContainsKey(guildInfo.GetGuildId()) == true)
                {
                    searchGuildClone.joinRequest = true;
                }

                searchGuildList.Add(searchGuildClone);

                if (++count >= K2Const.MAX_GUILD_SEARCH_COUNT)
                {
                    break;
                }
            }

            SendGuildSearchResult(ePacketCommonResult.Success, eGuildSearchType.Ranking, requesterInfo, searchGuildList);
        }

        private void DbZMQGuildSearchQuery(eGuildSearchType type, List<long> guildIdList, GuildRequesterData requesterInfo)
        {
            var packet = new CS_ZMQ_GuildSearchQuery();
            packet.type = type;
            if (guildIdList != null)
            {
                packet.guildIdList.AddRange(guildIdList);
            }

            if (requesterInfo != null)
            {
                packet.requesterInfo.Copy(requesterInfo);
            }

            ServerModule.Instance.Controller.SendZMQ(eServerType.Database, packet, eServerType.Community);
        }

        public void RecvZMQGuildSearchQuery(ePacketCommonResult result, eGuildSearchType type, List<GuildSearchInfoPacket> guildInfoList, GuildRequesterData requesterInfo)
        {
            switch (type)
            {
                case eGuildSearchType.Name:
                    SendGuildSearchResultByName(result, requesterInfo, guildInfoList);
                    break;
            }
        }

        private void SendGuildSearchResultByName(ePacketCommonResult result, GuildRequesterData requesterInfo, List<GuildSearchInfoPacket> guildInfoList)
        {
            List<GuildSearchInfoPacket> searchGuildList = null;
            if (result != ePacketCommonResult.Success)
            {
                requesterInfo.GetParamValue(eGuildParamType.GuildName, out string guildName);
                SLogManager.Instance.ErrorLog($"guildSearchByName fail - charId:{requesterInfo.charId}, name:{guildName}, result:{result}");

                SendGuildSearchResult(result, eGuildSearchType.Name, requesterInfo, searchGuildList);
                return;
            }

            // 요청자가 가입한 길드를 찾기 위한 dictionary
            _guildJoinByCharIdDic.TryGetValue(requesterInfo.charId, out var recruitDic);

            searchGuildList = new List<GuildSearchInfoPacket>();
            var guildSearchEntity = new GuildSearchEntity();
            foreach (var info in guildInfoList)
            {
                guildSearchEntity.Copy(info);

                IGuildSearch guildInfo;
                if (_guildDic.TryGetValue(guildSearchEntity.id, out GuildCommunityModel guildModel) == true)
                {
                    guildInfo = guildModel.GetGuildInfo();
                }
                else
                {
                    guildInfo = guildSearchEntity;
                }

                if (guildInfo.GetJoinType() == eGuildJoinType.Disable)
                {
                    continue;
                }

                var searchGuildClone = guildInfo.GetSearchInfoClone();

                if (recruitDic != null && recruitDic.ContainsKey(guildInfo.GetGuildId()) == true)
                {
                    searchGuildClone.joinRequest = true;
                }

                searchGuildList.Add(searchGuildClone);
            }

            SendGuildSearchResult(result, eGuildSearchType.Name, requesterInfo, searchGuildList);
        }

        private void SendGuildSearchResultByJoinRequest(GuildRequesterData requesterInfo)
        {
            var now = DateTime.Now;
            var searchGuildList = new List<GuildSearchInfoPacket>();
            if (_guildJoinByCharIdDic.TryGetValue(requesterInfo.charId, out var recruitDic) == true)
            {
                IGuildSearch guildInfo;
                foreach (var recruitEntity in recruitDic.Values)
                {
                    if (recruitEntity.IsValidPeriod(now) == false)
                    {
                        SendZMQGuildInfoSyncSave(eGuildInfoSyncSaveType.InvalidRequestClearByChar, 0, requesterInfo, null, null, null, null);
                        continue;
                    }

                    if (_guildDic.TryGetValue(recruitEntity.guildId, out GuildCommunityModel guildModel) == true)
                    {
                        guildInfo = guildModel.GetGuildInfo();
                        var clone = guildInfo.GetSearchInfoClone();
                        clone.SetJoinRequestTime(recruitEntity.regDate);
                        searchGuildList.Add(clone);
                    }
                }
            }

            SendGuildSearchResult(ePacketCommonResult.Success, eGuildSearchType.JoinRequest, requesterInfo, searchGuildList);
        }

        public void ReqGuildCharacterSearch(ePacketCommonResult result, eGuildActionType actionType, GuildRequesterData requesterInfo, GuildEnermyInfoPacket enermyInfo)
        {
            ePacketCommonResult commonResult = ePacketCommonResult.Success;

            do
            {
                switch (result)
                {
                    case ePacketCommonResult.NotFoundCharacter:
                        commonResult = result;
                        break;
                    default:
                        var guildModel = GetGuildModel(requesterInfo.charId);
                        if (guildModel != null && guildModel.GetGuildState() != eGuildState.Dissolved)
                        {
                            switch (actionType)
                            {
                                case eGuildActionType.AddEnermy:
                                    ReqGuildAddEnermy(requesterInfo, guildModel, enermyInfo);
                                    break;

                                case eGuildActionType.RemoveEnermy:
                                    ReqGuildRemoveEnemy(requesterInfo, guildModel, enermyInfo);
                                    break;
                            }
                        }
                        else
                        {
                            result = ePacketCommonResult.NotGuildJoined;
                        }
                        break;
                }
            }
            while (false);

            if (commonResult != ePacketCommonResult.Success)
            {
                SendGuildActionResult(result, actionType, requesterInfo, null);
            }
        }
        #endregion


        #region guild skill
        public ePacketCommonResult ReqGuildSkillAdd(GuildRequesterData requesterInfo, GuildCommunityModel guild)
        {
            var result = ePacketCommonResult.Success;
            var requesterBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(requesterInfo.charId);
            if (null == requesterBBModel)
            {
                return ePacketCommonResult.NotFoundBBUser;
            }

            requesterInfo.GetParamValue(eGuildParamType.GuildSkillId, out long guildSkillId);
            GuildSkillEntity newSkillEntity;
            result = guild.GuildSkillAdd((int)guildSkillId, out newSkillEntity);
            List<GuildSkillEntity> newSkillList = new List<GuildSkillEntity>();

            if (null != newSkillEntity)
            {
                newSkillList.Add(newSkillEntity);
            }

            SendGuildActionResultByGuildSkill(result, eGuildActionType.AddGuildSkill, requesterInfo, guild.GetGuildInfo(), newSkillList);

            return result;
        }

        public ePacketCommonResult ReqGuildSkillEnchant(GuildRequesterData requesterInfo, GuildCommunityModel guild)
        {
            var requesterBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(requesterInfo.charId);
            if (null == requesterBBModel)
            {
                return ePacketCommonResult.NotFoundBBUser;
            }

            requesterInfo.GetParamValue(eGuildParamType.GuildSkillId, out long guildSkillId);
            //requesterInfo.GetParamValue(eGuildParamType.GuildSkillEnchantLevel, out long guildEnchantLevel);
            requesterInfo.GetParamValue(eGuildParamType.CheatSkillRate, out long rateCheatState);

            var result = guild.GuildSkillEnchant(requesterInfo.charId, (int)guildSkillId, (eCheatRandomStateType)rateCheatState, out var updateSkillEntity);
            List<GuildSkillEntity> changeSkillList = new List<GuildSkillEntity>();

            bool isSuccess = false;
            if (null != updateSkillEntity)
            {
                changeSkillList.Add(updateSkillEntity);
                isSuccess = true;
            }

            requesterInfo.paramDic.Add(eGuildParamType.GuildSkillSuccess, new GuildParam(eGuildParamType.GuildSkillSuccess, isSuccess.ToString()));
            SendGuildActionResultByGuildSkill(result, eGuildActionType.EnchantGuildSkill, requesterInfo, guild.GetGuildInfo(), changeSkillList);

            return result;
        }

        public ePacketCommonResult SaveGuildSkillSlot(long guildId, long requesterId, int skillSlot0, int skillSlot1, int skillSlot2)
        {
            if (false == _guildDic.TryGetValue(guildId, out var guild))
            {
                return ePacketCommonResult.InternalError;
            }

            var result = guild.SaveGuildSkillSlot(requesterId, skillSlot0, skillSlot1, skillSlot2);
            if (ePacketCommonResult.Success == result)
            {
                var sendPacket = new SC_ZMQ_SaveGuildSkillSlot();
                sendPacket.result = result;
                sendPacket.guildId = guildId;
                sendPacket.requesterId = requesterId;
                sendPacket.skillSlot.Copy(guild.GetGuildSkillSlotEntity());

                BroadcastZMQToGameNode(sendPacket);
            }

            return result;
        }

        private void SendGuildActionResultByGuildSkill(ePacketCommonResult result, eGuildActionType actionType, GuildRequesterData requesterInfo, GuildEntity guildEntity, List<GuildSkillEntity> skillList)
        {
            var guildInfo = new GuildInfoData();

            if (guildEntity != null)
            {
                guildInfo.guild.Copy(guildEntity);
            }

            if (skillList != null)
            {
                guildInfo.skillList.AddRange(skillList);
            }

            SendGuildActionResult(result, actionType, requesterInfo, guildInfo);
        }
        #endregion guild skill


        public void CheckCharacterLogin(long charId)
        {
            var guildModel = GetGuildModel(charId);
            if (guildModel != null)
            {
                var member = guildModel.GetGuildMemberInfo(charId);
                if (member != null)
                {
                    SendZMQGuildNotify(eGuildNotifyType.MemberConnect, guildModel.guildId, member.charId, member.charName, string.Empty);
                    {
                        var cmd = new ChattingCommunityInfoCmd();
                        cmd.type = eUpdateCommunityType.CommunityUpdate;
                        cmd.charId = charId;
                        CommunityWorkerManager.Instance.AddCommand(cmd);
                    }
                }
            }
        }

        public void CheckCharacterLogout(long charId)
        {
            var guildModel = GetGuildModel(charId);
            if (guildModel != null)
            {
                var member = guildModel.GetGuildMemberInfo(charId);
                if (member != null)
                {
                    member.SetLogoutTime(K2Common.GetDateTime());
                    SendZMQGuildNotify(eGuildNotifyType.MemberDisconnect, guildModel.guildId, member.charId, member.charName, string.Empty);
                    // 채팅 로그 아웃은 게이트웨이에서
                }
            }
            //guildModel?.CheckCharacterLogout(charId);
        }

        public void SendZMQGuildNotify(eGuildNotifyType type, long guildId, long charId, string name, string msg)
        {
            var packet = new SC_ZMQ_GuildNotify();
            packet.type = type;
            packet.guildId = guildId;
            packet.targetCharId = charId;
            packet.targetName = name;
            packet.msg = msg;

            //ServerModule.Instance.GetServerController().BroadcastZMQ(eServerType.Game, _myServerGroupId, packet);
            BroadcastZMQToGameNode(packet);
        }

        #region guildplace
        public ePacketCommonResult GuildPlaceRent(long charId, eGuildPlaceActionType type,
            ref GuildPlaceEntity entity)
        {
            if (_guildByCharIdDic.TryGetValue(charId, out long guildId) == false)
                return ePacketCommonResult.NotGuildJoined;

            if (_guildDic.TryGetValue(guildId, out GuildCommunityModel guild) == false)
                return ePacketCommonResult.NotFoundGuild;

            if (guild.GetGuildInfo().IsMaster(charId) == false)
                return ePacketCommonResult.Place_NoPermissionRent;

            var result = ePacketCommonResult.None;
            switch (type)
            {
                case eGuildPlaceActionType.Rent:
                    result = guild.Rent();
                    break;

                case eGuildPlaceActionType.Extension:
                    result = guild.Extension();
                    break;
            }

            if (result == ePacketCommonResult.Success)
                entity.Copy(guild.GetPlace());

            return result;
        }

        public void BroadcastGuildPlaceZone(long guildId, GuildPlaceZone placeZone)
        {
            var packet = new SC_ZMQ_GuildPlaceSetZone();
            packet.guildId = guildId;
            packet.placeZone.Set(placeZone);
            BroadcastZMQToGameNode(packet);
        }

        public void SetGuildPlaceZone(long guildId, GuildPlaceZone placeZone)
        {
            if (_guildDic.TryGetValue(guildId, out GuildCommunityModel guild) == false)
            {
                SLogManager.Instance.ErrorLog($"guild is null - guildId({guildId}), serverId({placeZone.serverId}), zoneId({placeZone.zoneId}), zoneChannelKey({placeZone.zoneChannelKey})");

                return;
            }

            if (guild.SetGuildPlaceZone(placeZone))
            {
                BroadcastGuildPlaceZone(guild.guildId, guild.GetPlaceZone());
            }
            else
            {
                // 유저가 최초 입장하는 사이 아지트의 상태 변경이 발생
                var zoneForceChangeType = guild.DequeueGuildPlaceClearFlag();
                if (zoneForceChangeType == eZoneForceChangeType.None)
                    return;

                var packet = new SC_ZMQ_GuildPlaceKickOutAllZone();
                packet.guildId = guild.guildId;
                packet.zoneForceChangeType = zoneForceChangeType;
                packet.placeZone.Set(placeZone);
                ServerModule.Instance.Controller.SendZMQ(eServerType.Game, packet, eServerType.Community, placeZone.serverId);
            }
        }

        public void ClearGuildPlaceZone(long guildId, eZoneForceChangeType zoneForceChangeType)
        {
            if (_guildDic.TryGetValue(guildId, out GuildCommunityModel guild) == false)
            {
                SLogManager.Instance.ErrorLog($"guild is null - guildId({guildId}), zoneForceChangeType({zoneForceChangeType})");

                return;
            }

            GuildPlaceZone placeZone = null;
            if (guild.CheckPlaceZoneClearFlag(zoneForceChangeType))
            {
                placeZone = guild.GetPlaceZone().Clone();
                guild.GetPlaceZone().Clear();

                BroadcastGuildPlaceZone(guild.guildId, guild.GetPlaceZone());
                guild.SendGuildPlaceKickOutAllZone(placeZone, zoneForceChangeType);
            }
        }

        public GuildPlaceZone GetGuildPlaceZone(long guildId)
        {
            if (_guildDic.TryGetValue(guildId, out GuildCommunityModel guild) == false)
            {
                SLogManager.Instance.ErrorLog($"guild is null - guildId({guildId})");
                return null;
            }

            return guild.GetPlaceZone();
        }

        public void RepGuildPlaceTeleportInfo(long requesterCharId, int teleportGroupID, int serverId)
        {
            var sendPacket = new SC_ZMQ_GuildPlaceTeleportInfo();
            sendPacket.requesterCharId = requesterCharId;
            sendPacket.result = ePacketCommonResult.Success;

            do
            {
                if (_guildByCharIdDic.TryGetValue(requesterCharId, out long guildId) == false)
                {
                    sendPacket.result = ePacketCommonResult.NotFoundGuild;
                    break;
                }

                if (_guildDic.TryGetValue(guildId, out GuildCommunityModel guild) == false)
                {
                    sendPacket.result = ePacketCommonResult.NotGuildJoined;
                    break;
                }

                var guildPlaceDelayTime = DataManager.Get<LoaderConstantConfig>().GetConstantCofigData("GuildPlaceExpireTime");
                if (guild.GetPlace().expireTime.AddSeconds(guildPlaceDelayTime.Value1) <= K2Common.GetDateTime())
                {
                    sendPacket.result = ePacketCommonResult.Place_NotRent;
                    break;
                }

                var guildPlaceData = DataManager.Get<LoaderGuildPlace>().GetGuildPlace(guild.GetPlace().placeId);
                if (guildPlaceData == null)
                {
                    sendPacket.result = ePacketCommonResult.Place_NotFoundInfo;
                    break;
                }

                var teleportLinkGroupData = DataManager.Get<LoaderNpcData>().GetTeleportLinkGroupData(teleportGroupID, (int)guildPlaceData.GuildPlaceGrade);
                if (teleportLinkGroupData == null)
                {
                    sendPacket.result = ePacketCommonResult.InvalidTeleportLinkId;
                    break;
                }

                sendPacket.teleportLinkId = teleportLinkGroupData.TeleportLinkId;
            }
            while (false);

            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, serverId);
        }
        #endregion

        public DateTime lastUpdateCheckTime = default(DateTime);
        public void Update(DateTime now)
        {
            if (lastUpdateCheckTime.AddSeconds(Defines.TICK_GUILD_UPDATE_AND_SYNC_PERIOD_SEC) <= now)
            {
                SyncGuildData(now);
                lastUpdateCheckTime = now;
            }

            CheckDelayedEvent();

            foreach (var guildModel in _guildDic.Values)
            {
                guildModel.UpdateInviteExpireTimer(now);
                if (guildModel.CheckPlaceExpireTime(now))
                    ClearGuildPlaceZone(guildModel.guildId, eZoneForceChangeType.GuildPlace_TimeOut);
            }
        }

        public void SyncGuildData(DateTime now)
        {
            var dbPacket = new CS_ZMQ_GuildInfoAsyncSave();
            var gamePacket = new SC_ZMQ_GuildInfoSync();

            foreach (var guildModel in _guildDic.Values)
            {
                this.MakeSaveDbPacket(now, guildModel, ref dbPacket, ref gamePacket);
            }

            if (dbPacket.guildInfoDic.Count > 0)
            {
                ServerModule.Instance.Controller.SendZMQ(eServerType.Database, dbPacket, eServerType.Community);
            }

            if (gamePacket.guildInfoList.Count > 0)
            {
                BroadcastZMQToGameNode(gamePacket);
                //ServerModule.Instance.GetServerController().BroadcastZMQ(eServerType.Game, _myServerGroupId, gamePacket);
            }
        }

        public void SyncGuildData(GuildCommunityModel guildModel)
        {
            var dbPacket = new CS_ZMQ_GuildInfoAsyncSave();
            var gamePacket = new SC_ZMQ_GuildInfoSync();

            this.MakeSaveDbPacket(DateTime.Now, guildModel, ref dbPacket, ref gamePacket);

            if (dbPacket.guildInfoDic.Count > 0)
            {
                ServerModule.Instance.Controller.SendZMQ(eServerType.Database, dbPacket, eServerType.Community);
            }

            if (gamePacket.guildInfoList.Count > 0)
            {
                BroadcastZMQToGameNode(gamePacket);
                //ServerModule.Instance.GetServerController().BroadcastZMQ(eServerType.Game, _myServerGroupId, gamePacket);
            }
        }

        public bool IsExistLoadingGuild(long guildId, long relationGuildId)
        {
            var remainGuildIdList = _delayedRelationLoadEvent.GetRemainEventList(guildId);
            return (remainGuildIdList != null && remainGuildIdList.Contains(relationGuildId) == true);
        }

        private void OnCompleteLoadGuild(long guildId, bool isReset)
        {
            _notCompleteLoadGuildIdSet.Remove(guildId);

            if (isReset == false)
            {
                SLogManager.Instance.InfoLog("OnCompleteLoadGuild()", "complete to load relation guild - guildId({0})", guildId);
            }
        }

        private void CheckDelayedEvent()
        {
            foreach (var guildId in _notCompleteLoadGuildIdSet)
            {
                _delayedRelationLoadEvent.CheckExpiredEvent(guildId, () =>
                {
                    var remainGuildIdList = _delayedRelationLoadEvent.GetRemainEventList(guildId);
                    foreach (var targetGuildId in remainGuildIdList)
                    {
                        // 재시도 하기 애매해서 동맹, 적대 길드 안읽힌거 로그 표시
                        if (_guildDic.ContainsKey(guildId) == false)
                        {
                            SLogManager.Instance.ErrorLog($"failed to load alliance, hostile guild - guildId({guildId}), targetGuildId({targetGuildId})");
                        }
                    }
                });
            }
        }

        private void BroadcastZMQToGameNode(ZmqPacket packet)
        {
            ServerModule.Instance.Controller.BroadcastZMQ(eServerType.Game, _myServerGroupId, packet);

            if (Global.isDevelopMode == false)
            {
                ServerModule.Instance.Controller.BroadcastZMQ(eServerType.Inter, _myServerGroupId, packet);
            }
        }

        private void BroadcastZMQToChattingNode(ZmqPacket packet)
        {
            ServerModule.Instance.Controller.BroadcastZMQ(eServerType.Chatting, _myServerGroupId, packet);
        }

        private void MakeSaveDbPacket(in DateTime now, GuildCommunityModel guildModel, ref CS_ZMQ_GuildInfoAsyncSave dbPacket, ref SC_ZMQ_GuildInfoSync gamePacket)
        {
            var guildData = guildModel.GetSaveToDB(now, out var isChange);
            if (guildModel._isModify == true || guildModel._needForceSave == true || isChange == true)
            {
                guildModel.CheckUpdatedEntity(guildData);
                guildModel.SaveComplete();

                dbPacket.guildInfoDic.Add(guildModel.guildId, guildData.Clone());
                gamePacket.guildInfoList.Add(guildData);
            }
        }
    }
}
