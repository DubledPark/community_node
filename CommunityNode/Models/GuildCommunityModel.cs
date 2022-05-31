using K2Packet.DataStruct;
using K2Server.Database.Entities;
using K2Server.Managers;
using K2Server.Models;
using K2Server.Packet.Protocol;
using K2Server.ServerNodes.CommunityNode.Commands;
using K2Server.ServerNodes.CommunityNode.Managers;
using K2Server.Util;
using RRCommon.Enum;
using SharedLib;
using SharedLib.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace K2Server.ServerNodes.CommunityNode.Models
{
    public class GuildCommunityModel : GuildModel
    {
        public bool _needForceSave;
        public bool _isModify;
        public DateTime _lastDBUpdateTime;
        public DateTime _lastResetCheckTime;

        // guild skill
        DateTime _lastUpdateGuildSkill = default;
        long _lastUpdateGuildSkillMemberId;  // charId

        // cheat
        private int _cheatMaxMemberCount;

        // <charId, GuildMemberEntity>
        protected Dictionary<long, GuildMemberEntity> _memberInfoDic = new Dictionary<long, GuildMemberEntity>();

        // <guildId, GuildRelationEntity>
        protected Dictionary<long, GuildRelationEntity> _guildRelationDic = new Dictionary<long, GuildRelationEntity>();

        // <charId, GuildRelationEntity>
        protected Dictionary<long, GuildRelationEntity> _enermyRelationDic = new Dictionary<long, GuildRelationEntity>();

        // <crestId, GuildCrestEntity>
        protected Dictionary<int, GuildCrestEntity> _collectedCrestDic = new Dictionary<int, GuildCrestEntity>();

        protected Dictionary<int, GuildSkillEntity> _guildSkillDic = new Dictionary<int, GuildSkillEntity>();

        protected GuildSkillSlotEntity _guildSkillSlot = new GuildSkillSlotEntity();

        protected GuildPlaceEntity _guildPlace = new GuildPlaceEntity();
        protected GuildPlaceZone _guildPlaceZone = new GuildPlaceZone();
        
        protected eZoneForceChangeType _guildPlaceClearFlag;

        protected Dictionary<long, DateTime> _joinReservedDic = new Dictionary<long, DateTime>();

        public override void Reset()
        {
            base.Reset();

            _needForceSave = false;
            _isModify = false;
            _lastDBUpdateTime = default(DateTime);
            _lastResetCheckTime = default(DateTime);

            _lastUpdateGuildSkill = default;
            _lastUpdateGuildSkillMemberId = 0;

            _memberInfoDic.ClearWithReturnPool();
            _guildRelationDic.ClearWithReturnPool();
            _enermyRelationDic.ClearWithReturnPool();
            _collectedCrestDic.ClearWithReturnPool();
            _guildSkillDic.ClearWithReturnPool();
            _guildSkillSlot.Clear();
            _joinReservedDic.Clear();

            _guildPlace.Clear();
            _guildPlaceZone.Clear();

            _cheatMaxMemberCount = 0;
        }

        //public override void Init(GuildInfoData guildInfo)
        //{
        //    base.Init(guildInfo);

        //    var maxExp = DataManager.Get<LoaderGuildLevel>().GetGuildMaxExp();
        //    var nextLevelExp = DataManager.Get<LoaderGuildLevel>().GetGuildNextLevelExp(_guildEntity.level);
        //}

        public GuildInfoData GetSaveToDB(DateTime now, out bool isChange)
        {
            var _tmpGuildInfo = new GuildInfoData();
            isChange = false;

            // SaveToDB 를 더 공용적으로 변경 필요
            isChange |= DbEntityManager.SaveToDB(_guildEntity, ref _tmpGuildInfo.guild, false);

            isChange |= DbEntityManager.SaveToDB(_guildSkillSlot, ref _tmpGuildInfo.skillSlot, false);

            foreach (var memberEntity in _memberInfoDic.Values)
            {
                if (memberEntity.GetDBEntityState().IsModified() == true)
                {
                    memberEntity.PackValues();
                }

                isChange |= DbEntityManager.SaveToDB(memberEntity, ref _tmpGuildInfo.memberList, false);
            }

            foreach (var relationEntity in _guildRelationDic.Values)
            {
                if (relationEntity.IsValidPeriod(now) == false)
                {
                    relationEntity.Delete();
                }

                isChange |= DbEntityManager.SaveToDB(relationEntity, ref _tmpGuildInfo.relationList, false);
            }

            foreach (var relationEntity in _enermyRelationDic.Values)
            {
                isChange |= DbEntityManager.SaveToDB(relationEntity, ref _tmpGuildInfo.relationList, false);
            }

            foreach (var crestEntity in _collectedCrestDic.Values)
            {
                isChange |= DbEntityManager.SaveToDB(crestEntity, ref _tmpGuildInfo.collectedCrestList, false);
            }

            foreach (var skillEntity in _guildSkillDic.Values)
            {
                isChange |= DbEntityManager.SaveToDB(skillEntity, ref _tmpGuildInfo.skillList, false);
            }

            isChange |= DbEntityManager.SaveToDB(_guildPlace, ref _tmpGuildInfo.place, false);

            return _tmpGuildInfo;
        }

        public void CheckUpdatedEntity(GuildInfoData guildInfo)
        {
            // async 처리에서 삭제가 필요한 데이터는 여기에서 처리
            foreach (var relation in guildInfo.relationList)
            {
                if (relation.IsVaild() == false)
                    RemoveRelationEntity(relation);
            }
        }

        public ePacketCommonResult ReqGuildBanish(long requestCharId, long targetCharId, out GuildMemberEntity outputMemberEntity, out GuildRecruitEntity outputRecruitEntity)
        {
            outputMemberEntity = null;
            outputRecruitEntity = null;

            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
                return ePacketCommonResult.NotFoundGuildMember;

            if (_memberInfoDic.TryGetValue(targetCharId, out GuildMemberEntity targetEntity) == false)
                return ePacketCommonResult.NotFoundGuildMember;

            // requestEntity 의 권한 체크
            // grade가 낮은 숫자가 더 높은 등급
            if (requestEntity.grade >= targetEntity.grade)
                return ePacketCommonResult.NoPermissionGuildAction;

            var clone = targetEntity.Clone();
            clone.Delete();
            outputMemberEntity = clone;

            outputRecruitEntity = new GuildRecruitEntity();
            outputRecruitEntity.Init(targetEntity.charId, targetEntity.charName, targetEntity.charTableId, 0, string.Empty);

            return ePacketCommonResult.Success;
        }

        public ePacketCommonResult ReqGuildLeave(long requestCharId, out GuildMemberEntity outputMemberEntity, out GuildRecruitEntity outputRecruitEntity)
        {
            outputMemberEntity = null;
            outputRecruitEntity = null;

            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
                return ePacketCommonResult.NotFoundGuildMember;

            // requestEntity 의 권한 체크
            if (requestEntity.grade == (byte)eGuildMemberGrade.Master)
                return ePacketCommonResult.CannotLeaveGuildMaster;

            var clone = requestEntity.Clone();
            clone.Delete();
            outputMemberEntity = clone;

            outputRecruitEntity = new GuildRecruitEntity();
            outputRecruitEntity.Init(requestEntity.charId, requestEntity.charName, requestEntity.charTableId, 0, string.Empty);

            return ePacketCommonResult.Success;
        }

        public ePacketCommonResult ReqGuildDissolve(long requestCharId, out GuildEntity outputGuildEntity, out GuildMemberEntity outputMemberEntity, out GuildRecruitEntity outputRecruitEntity,
            out List<GuildRelationEntity> outputRelationEntity)
        {
            outputGuildEntity = null;
            outputMemberEntity = null;
            outputRecruitEntity = null;
            outputRelationEntity = null;

            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity memberEntity) == false)
                return ePacketCommonResult.NotFoundGuildMember;

            // requestEntity 의 권한 체크
            if (_memberInfoDic.Count > 1)
                return ePacketCommonResult.CannotLeaveGuildMaster;

            var allianceCount = GetGuildRelationCount(eGuildRelationType.Alliance);
            if (allianceCount > 0)
                return ePacketCommonResult.CannotDissolveGuildByAlliance;

            var memberClone = memberEntity.Clone();
            memberClone.Delete();
            outputMemberEntity = memberClone;

            var guildClone = _guildEntity.Clone();
            guildClone.Delete();
            outputGuildEntity = guildClone;

            outputRecruitEntity = new GuildRecruitEntity();
            outputRecruitEntity.Init(memberEntity.charId, memberEntity.charName, memberEntity.charTableId, 0, string.Empty);

            var relationList = GetGuildRelationList().Select(x => x.Clone()).ToList();
            var enermyList = GetGuildEnermyList().Select(x => x.Clone()).ToList();

            outputRelationEntity = relationList;
            outputRelationEntity.AddRange(enermyList);

            return ePacketCommonResult.Success;
        }

        public ePacketCommonResult ReqGuildSettingChange(GuildRequesterData requestInfo)
        {
            if (_memberInfoDic.TryGetValue(requestInfo.charId, out GuildMemberEntity requestEntity) == false)
                return ePacketCommonResult.NotFoundGuildMember;

            // requestEntity 의 권한 체크
            if (requestEntity.grade != (byte)eGuildMemberGrade.Master)
                return ePacketCommonResult.NoPermissionGuildAction;

            bool isModify = false;
            if (true == requestInfo.GetParamValue(eGuildParamType.JoinType, out long joinTypeValue))
            {
                requestInfo.GetParamValue(eGuildParamType.Password, out long password);
                if (joinTypeValue != (int)eGuildJoinType.None)
                    isModify |= _guildEntity.UpdateJoinType((eGuildJoinType)joinTypeValue, (int)password);
            }

            if (true == requestInfo.GetParamValue(eGuildParamType.Introduce, out string introduce))
            {
                var res = _guildEntity.UpdateIntroduce(introduce);
                isModify |= res;

                if (res == true)
                    GuildCommunityManager.Instance.GuildRankingUpdate(requestInfo.charId, _guildEntity, false);
            }

            if (true == requestInfo.GetParamValue(eGuildParamType.MasterTitle, out string masterTitle))
                isModify |= _guildEntity.UpdateMasterTitle(masterTitle);

            if (true == requestInfo.GetParamValue(eGuildParamType.ViceMasterTitle, out string viceMasterTitle))
                isModify |= _guildEntity.UpdateViceMasterTitle(viceMasterTitle);

            if (true == requestInfo.GetParamValue(eGuildParamType.MemberTitle, out string memberTitle))
                isModify |= _guildEntity.UpdateMemberTitle(memberTitle);

            if (isModify == true)
                Modify();

            return ePacketCommonResult.Success;
        }

        public ePacketCommonResult ReqGuildGive(long requestCharId, int giveId, out long addExp)
        {
            addExp = 0;

            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
                return ePacketCommonResult.NotFoundGuildMember;

            requestEntity.CheckRefresh(K2Common.GetDateTime());
            // 기부 횟수 체크
            var data = DataManager.Get<LoaderGuildGive>().GetGuildGiveData(giveId);
            if (data == null)
                return ePacketCommonResult.InvalidGuildParam;

            if (requestEntity.CheckGiveCount(giveId, data.DailyMaxCount) == false)
                return ePacketCommonResult.OverCountGuildGive;

            requestEntity.AddGive(giveId, data.RewardContribution, data.GetGuildExp);

            // 경험치 체크
            addExp = data.GetGuildExp;
            Modify();

            return ePacketCommonResult.Success;
        }

        public ePacketCommonResult ReqAddCrest(long requestCharId, int crestId, out List<GuildCrestEntity> crestList, out bool isOverlapped, out long addExp)
        {
            isOverlapped = false;
            crestList = null;
            addExp = 0;
            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
                return ePacketCommonResult.NotFoundGuildMember;

            var crestData = DataManager.Get<LoaderGuildCrest>().GetGuildCrestData(crestId);
            if (crestData == null)
            {
                SLogManager.Instance.ErrorLog($"not exist guildCrestData - guildId:{guildId}, crestId:{crestId}");
                return ePacketCommonResult.InternalError;
            }

            if (_collectedCrestDic.ContainsKey(crestId) == true)
            {
                // 경험치 체크
                isOverlapped = true;
                addExp = crestData.OverlapGuildExp;
            }
            else
            {
                AddGuildCrest(crestId, out var reqCrest);
                crestList = new List<GuildCrestEntity>()
                {
                    reqCrest
                };
            }

            Modify();

            return ePacketCommonResult.Success;
        }

        /// <summary>
        /// 길드원 자기소개 수정
        /// </summary>
        /// <param name="requestCharId"></param>
        /// <param name="introduce"></param>
        /// <returns></returns>
        public ePacketCommonResult ReqGuildMemberIntroduceChange(long requestCharId, string introduce)
        {
            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
                return ePacketCommonResult.NotFoundGuildMember;

            if (requestEntity.UpdateIntroduce(introduce) == true)
                Modify();

            return ePacketCommonResult.Success;
        }

        public ePacketCommonResult ReqGuildMemberQuestInitChange(long requestCharId, string isQuestInit)
        {
            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
                return ePacketCommonResult.NotFoundGuildMember;

            bool questInit = Convert.ToBoolean(isQuestInit);

            if (requestEntity.SetGuildQuestInit(questInit) == true)
            {
                Modify();
            }

            return ePacketCommonResult.Success;
        }

        public ePacketCommonResult ReqGuildCrestChange(long requestCharId, int crestId)
        {
            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
                return ePacketCommonResult.NotFoundGuildMember;

            if (requestEntity.grade != (int)eGuildMemberGrade.Master)
                return ePacketCommonResult.NoPermissionGuildAction;

            // crestId 유효성 체크
            if (DataManager.Get<LoaderGuildCrest>().IsUseableCrest(crestId, _guildEntity.level, _guildEntity.ranking) == false)
                return ePacketCommonResult.InvalidGuildCrestId;

            if (_guildEntity.UpdateCrestId(crestId) == true)
            {
                Modify();
                GuildCommunityManager.Instance.GuildRankingUpdate(requestCharId, _guildEntity, false);
            }

            return ePacketCommonResult.Success;
        }

        /// <summary>
        /// 길드 공지 수정
        /// </summary>
        /// <param name="requestCharId"></param>
        /// <param name="notice"></param>
        /// <returns></returns>
        public ePacketCommonResult ReqGuildNoticeChange(long requestCharId, string notice)
        {
            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
                return ePacketCommonResult.NotFoundGuildMember;

            if (requestEntity.grade != (int)eGuildMemberGrade.Master &&
                requestEntity.grade != (int)eGuildMemberGrade.ViceMaster)
                return ePacketCommonResult.NoPermissionGuildAction;

            if (_guildEntity.UpdateNotice(notice) == true)
                Modify();

            return ePacketCommonResult.Success;
        }

        /// <summary>
        /// 길드원 등급 수정
        /// </summary>
        /// <param name="requestCharId"></param>
        /// <param name="targetCharId"></param>
        /// <param name="grade"></param>
        /// <returns></returns>
        public ePacketCommonResult ReqGuildMemberGradeChange(long requestCharId, long targetCharId, int grade)
        {
            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
            {
                return ePacketCommonResult.NotFoundGuildMember;
            }

            if (_memberInfoDic.TryGetValue(targetCharId, out GuildMemberEntity targetEntity) == false)
            {
                return ePacketCommonResult.NotFoundGuildMember;
            }

            // requestEntity 의 권한 체크
            if (requestEntity.grade != (int)eGuildMemberGrade.Master)
            {
                return ePacketCommonResult.NoPermissionGuildAction;
            }

            if (grade == (int)eGuildMemberGrade.Master)
            {
                // 대상자의 level 체크
                var targetBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(targetCharId);
                if (targetBBModel == null)
                {
                    return ePacketCommonResult.CannotGuildGradeChangeLogoffUser;
                }

                var guildCreateLevel = DataManager.Get<LoaderGuildOption>().GetGuildOptionData(eOptionType.FoundLevel);
                if (guildCreateLevel != null &&
                    targetBBModel.userInfoData.level < guildCreateLevel.OptionValue)
                {
                    return ePacketCommonResult.CannotGuildGradeChangeLowLevel;
                }

                requestEntity.UpdateGrade((int)eGuildMemberGrade.Member);
                targetEntity.UpdateGrade((int)eGuildMemberGrade.Master);

                _guildEntity.UpdateMaster(targetEntity.charId, targetEntity.charName, targetEntity.charTableId);

                Modify(true);

                GuildCommunityManager.Instance.GuildRankingUpdate(requestCharId, _guildEntity, false);
                RankingCommunityManager.Instance.ReqGuildMasterClassChange(guildId, targetEntity.charTableId);
            }
            else if (grade == (int)eGuildMemberGrade.ViceMaster)
            {
                // 현재 ViceMaster 인원 제한 체크
                var viceMemberCount = _memberInfoDic.Values.Where(x => x.grade == (int)eGuildMemberGrade.ViceMaster).Count();

                if (viceMemberCount >= K2Const.MAX_GUILD_VICEMASTER_COUNT)
                {
                    return ePacketCommonResult.OverCountGuildViceMasterCount;
                }

                if (targetEntity.UpdateGrade((int)eGuildMemberGrade.ViceMaster) == false)
                {
                    return ePacketCommonResult.InvalidGuildParam;
                }

                Modify();
            }
            else if (grade == (int)eGuildMemberGrade.Member)
            {
                if (targetEntity.UpdateGrade((int)eGuildMemberGrade.Member) == false)
                {
                    return ePacketCommonResult.InvalidGuildParam;
                }

                Modify();
            }

            return ePacketCommonResult.Success;
        }

        /// <summary>
        /// 길드 동맹 요청
        /// </summary>
        /// <param name="requestCharId"></param>
        /// <param name="targetGuildModel"></param>
        /// <param name="relationList"></param>
        /// <returns></returns>
        public ePacketCommonResult ReqGuildAlliance(long requestCharId, GuildCommunityModel targetGuildModel, out List<GuildRelationEntity> relationList)
        {
            relationList = null;
            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
                return ePacketCommonResult.NotFoundGuildMember;

            if (requestEntity.grade != (int)eGuildMemberGrade.Master &&
                requestEntity.grade != (int)eGuildMemberGrade.ViceMaster)
                return ePacketCommonResult.NoPermissionGuildAction;

            var maxAllianceCount = DataManager.Get<LoaderGuildOption>().GetGuildOptionData(eOptionType.MaxAlliance);
            if (maxAllianceCount.OptionValue <= GetGuildRelationCount(eGuildRelationType.AllianceInvite))
                return ePacketCommonResult.OverCountGuildAllianceInvite;

            if (maxAllianceCount.OptionValue <= targetGuildModel.GetGuildRelationCount(eGuildRelationType.AllianceAccept))
                return ePacketCommonResult.OverCountGuildAllianceInvite;

            if (maxAllianceCount.OptionValue <= targetGuildModel.GetGuildRelationCount(eGuildRelationType.Alliance))
                return ePacketCommonResult.OverCountGuildAllianceAccept;

            if (IsRelatedToGuild(targetGuildModel.guildId, eGuildRelationType.Hostile) || targetGuildModel.IsRelatedToGuild(guildId, eGuildRelationType.Hostile))
                return ePacketCommonResult.CannotGuildAlianceHostile;

            if (targetGuildModel.IsRelatedToGuild(guildId, eGuildRelationType.Alliance, eGuildRelationType.AllianceInvite, eGuildRelationType.AllianceAccept))
                return ePacketCommonResult.AlreadyGuildAlliance;

            if (IsRelatedToGuild(targetGuildModel.guildId, eGuildRelationType.Alliance, eGuildRelationType.AllianceInvite, eGuildRelationType.AllianceAccept))
            {
                SLogManager.Instance.ErrorLog($"one side relationship exist. - guild:{guildId}, related guildId:{targetGuildModel.guildId}");
                return ePacketCommonResult.AlreadyGuildAlliance;
            }

            AddGuildRelation(eGuildRelationType.AllianceInvite, targetGuildModel.guildId
                , targetGuildModel.guildName, targetGuildModel.GetGuildInfo().crestId, out var reqRelation);
            Modify();

            targetGuildModel.AddGuildRelation(eGuildRelationType.AllianceAccept, guildId
                , guildName, this.GetGuildInfo().crestId, out var targetRelation);
            targetGuildModel.Modify();

            relationList = new List<GuildRelationEntity>()
            {
                reqRelation, targetRelation
            };

            return ePacketCommonResult.Success;
        }

        /// <summary>
        /// 길드 동맹 수락
        /// </summary>
        /// <param name="requestCharId"></param>
        /// <param name="targetGuildModel"></param>
        /// <param name="relationList"></param>
        /// <returns></returns>
        public ePacketCommonResult ReqGuildAllianceAccept(long requestCharId, GuildCommunityModel targetGuildModel, out List<GuildRelationEntity> relationList)
        {
            relationList = null;
            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
                return ePacketCommonResult.NotFoundGuildMember;

            if (requestEntity.grade != (int)eGuildMemberGrade.Master &&
                requestEntity.grade != (int)eGuildMemberGrade.ViceMaster)
                return ePacketCommonResult.NoPermissionGuildAction;

            var maxAllianceCount = DataManager.Get<LoaderGuildOption>().GetGuildOptionData(eOptionType.MaxAlliance);
            if (maxAllianceCount.OptionValue <= GetGuildRelationCount(eGuildRelationType.Alliance))
                return ePacketCommonResult.OverCountGuildAlliance;

            if (maxAllianceCount.OptionValue <= targetGuildModel.GetGuildRelationCount(eGuildRelationType.Alliance))
                return ePacketCommonResult.OverCountTargetGuildAlliance;

            if (IsRelatedToGuild(targetGuildModel.guildId, eGuildRelationType.Hostile) || targetGuildModel.IsRelatedToGuild(guildId, eGuildRelationType.Hostile))
                return ePacketCommonResult.CannotGuildAlianceHostile;

            if (IsRelatedToGuild(targetGuildModel.guildId, eGuildRelationType.Alliance) || targetGuildModel.IsRelatedToGuild(guildId, eGuildRelationType.Alliance))
                return ePacketCommonResult.AlreadyGuildAlliance;

            if (IsRelatedToGuild(targetGuildModel.guildId, eGuildRelationType.AllianceAccept) == false)
                return ePacketCommonResult.NotRequestGuildAlliance;

            if (targetGuildModel.IsRelatedToGuild(guildId, eGuildRelationType.AllianceInvite) == false)
                return ePacketCommonResult.NotRequestGuildAlliance;

            if (GetGuildRelation(eGuildRelationType.AllianceAccept, targetGuildModel.guildId, out var reqRelation) == false)
            {
                SLogManager.Instance.ErrorLog($"not exist guild relation. - guild:{guildId}, related guildId:{targetGuildModel.guildId}");
                return ePacketCommonResult.CannotFoundGuildAliance;
            }

            if (targetGuildModel.GetGuildRelation(eGuildRelationType.AllianceInvite, guildId, out var targetRelation) == false)
            {
                SLogManager.Instance.ErrorLog($"not exist guild relation. - guild:{targetGuildModel.guildId}, related guildId:{guildId}");
                return ePacketCommonResult.CannotFoundGuildAliance;
            }

            var now = K2Common.GetDateTime();
            if (reqRelation.IsValidPeriod(now) == false || targetRelation.IsValidPeriod(now) == false)
                return ePacketCommonResult.CannotFoundGuildAliance;

            reqRelation.UpdateRelateionType(eGuildRelationType.Alliance);
            targetRelation.UpdateRelateionType(eGuildRelationType.Alliance);

            Modify();
            targetGuildModel.Modify();

            relationList = new List<GuildRelationEntity>()
            {
                reqRelation, targetRelation
            };

            return ePacketCommonResult.Success;
        }

        /// <summary>
        /// 길드 동맹 거절
        /// </summary>
        /// <param name="requestCharId"></param>
        /// <param name="targetGuildModel"></param>
        /// <param name="relationList"></param>
        /// <returns></returns>
        public ePacketCommonResult ReqGuildAllianceReject(long requestCharId, GuildCommunityModel targetGuildModel, out List<GuildRelationEntity> relationList)
        {
            relationList = null;
            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
                return ePacketCommonResult.NotFoundGuildMember;

            if (requestEntity.grade != (int)eGuildMemberGrade.Master &&
                requestEntity.grade != (int)eGuildMemberGrade.ViceMaster)
                return ePacketCommonResult.NoPermissionGuildAction;

            if (IsRelatedToGuild(targetGuildModel.guildId, eGuildRelationType.Alliance, eGuildRelationType.AllianceAccept, eGuildRelationType.AllianceInvite) == false ||
                targetGuildModel.IsRelatedToGuild(guildId, eGuildRelationType.Alliance, eGuildRelationType.AllianceAccept, eGuildRelationType.AllianceInvite) == false)
            {
                return ePacketCommonResult.CannotFoundGuildAliance;
            }

            RemoveGuildRelation(targetGuildModel.guildId, out var reqRelation);
            Modify();

            targetGuildModel.RemoveGuildRelation(guildId, out var targetRelation);
            targetGuildModel.Modify();

            relationList = new List<GuildRelationEntity>()
            {
                reqRelation, targetRelation
            };

            return ePacketCommonResult.Success;
        }

        /// <summary>
        /// 길드 동맹 삭제
        /// </summary>
        /// <param name="requestCharId"></param>
        /// <param name="targetGuildModel"></param>
        /// <param name="relationList"></param>
        /// <returns></returns>
        public ePacketCommonResult ReqGuildAllianceRemove(long requestCharId, GuildCommunityModel targetGuildModel, out List<GuildRelationEntity> relationList)
        {
            relationList = null;
            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
                return ePacketCommonResult.NotFoundGuildMember;

            if (requestEntity.grade != (int)eGuildMemberGrade.Master &&
                requestEntity.grade != (int)eGuildMemberGrade.ViceMaster)
                return ePacketCommonResult.NoPermissionGuildAction;

            if (IsRelatedToGuild(targetGuildModel.guildId, eGuildRelationType.Alliance, eGuildRelationType.AllianceAccept, eGuildRelationType.AllianceInvite) == false ||
                targetGuildModel.IsRelatedToGuild(guildId, eGuildRelationType.Alliance, eGuildRelationType.AllianceAccept, eGuildRelationType.AllianceInvite) == false)
            {
                return ePacketCommonResult.CannotFoundGuildAliance;
            }

            RemoveGuildRelation(targetGuildModel.guildId, out var reqRelation);
            Modify();

            GuildCommunityManager.Instance.SyncGuildData(this);

            targetGuildModel.RemoveGuildRelation(guildId, out var targetRelation);
            targetGuildModel.Modify();

            GuildCommunityManager.Instance.SyncGuildData(targetGuildModel);

            relationList = new List<GuildRelationEntity>()
            {
                reqRelation, targetRelation
            };

            return ePacketCommonResult.Success;
        }

        /// <summary>
        /// 적재 길드 등록 요청
        /// </summary>
        /// <param name="requestCharId"></param>
        /// <param name="targetGuildModel"></param>
        /// <param name="relationList"></param>
        /// <returns></returns>
        public ePacketCommonResult ReqGuildAddHostile(long requestCharId, GuildCommunityModel targetGuildModel, out List<GuildRelationEntity> relationList)
        {
            relationList = null;
            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
            {
                return ePacketCommonResult.NotFoundGuildMember;
            }

            if (requestEntity.grade != (int)eGuildMemberGrade.Master &&
                requestEntity.grade != (int)eGuildMemberGrade.ViceMaster)
            {
                return ePacketCommonResult.NoPermissionGuildAction;
            }

            var maxHostileCount = DataManager.Get<LoaderGuildOption>().GetGuildOptionData(eOptionType.MaxHostile);
            if (maxHostileCount.OptionValue <= GetGuildRelationCount(eGuildRelationType.Hostile))
            {
                return ePacketCommonResult.OverCountGuildHostile;
            }

            if (IsRelatedToGuild(targetGuildModel.guildId, eGuildRelationType.Hostile))
            {
                return ePacketCommonResult.AlreadyGuildHostile;
            }

            if (IsRelatedToGuild(targetGuildModel.guildId, eGuildRelationType.Alliance, eGuildRelationType.AllianceInvite, eGuildRelationType.AllianceAccept) ||
                targetGuildModel.IsRelatedToGuild(guildId, eGuildRelationType.Alliance, eGuildRelationType.AllianceInvite, eGuildRelationType.AllianceAccept))
            {
                return ePacketCommonResult.CannotGuildHostileByAlliance;
            }

            AddGuildRelation(eGuildRelationType.Hostile, targetGuildModel.guildId
                , targetGuildModel.guildName, targetGuildModel.GetGuildInfo().crestId, out var reqRelation);
            Modify();

            relationList = new List<GuildRelationEntity>()
            {
                reqRelation
            };

            return ePacketCommonResult.Success;
        }

        /// <summary>
        /// 적대 길드 삭제 요청
        /// </summary>
        /// <param name="requestCharId"></param>
        /// <param name="targetGuildModel"></param>
        /// <param name="relationList"></param>
        /// <returns></returns>
        public ePacketCommonResult ReqGuildRemoveHostile(long requestCharId, GuildCommunityModel targetGuildModel, out List<GuildRelationEntity> relationList)
        {
            relationList = null;
            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
                return ePacketCommonResult.NotFoundGuildMember;

            if (requestEntity.grade != (int)eGuildMemberGrade.Master &&
                requestEntity.grade != (int)eGuildMemberGrade.ViceMaster)
                return ePacketCommonResult.NoPermissionGuildAction;

            if (IsRelatedToGuild(targetGuildModel.guildId, eGuildRelationType.Hostile) == false)
                return ePacketCommonResult.NotHostileGuild;

            RemoveGuildRelation(targetGuildModel.guildId, out var reqRelation);
            Modify();

            relationList = new List<GuildRelationEntity>()
            {
                reqRelation
            };

            return ePacketCommonResult.Success;
        }

        /// <summary>
        /// 척살 유저 등록 요청
        /// </summary>
        /// <param name="requestCharId"></param>
        /// <param name="enermyInfo"></param>
        /// <param name="relationList"></param>
        /// <returns></returns>
        public ePacketCommonResult ReqGuildAddEnermy(long requestCharId, GuildEnermyInfoPacket enermyInfo, out List<GuildRelationEntity> relationList)
        {
            relationList = null;
            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
            {
                return ePacketCommonResult.NotFoundGuildMember;
            }

            if (requestEntity.grade != (int)eGuildMemberGrade.Master &&
                requestEntity.grade != (int)eGuildMemberGrade.ViceMaster)
            {
                return ePacketCommonResult.NoPermissionGuildAction;
            }

            var maxHostileCount = DataManager.Get<LoaderGuildOption>().GetGuildOptionData(eOptionType.MaxEnemy);
            if (maxHostileCount.OptionValue <= GetGuildEnermyCount())
            {
                return ePacketCommonResult.OverCountGuildEnermy;
            }

            if (IsEnermyToGuild(enermyInfo.charId) == true)
            {
                return ePacketCommonResult.AlreadyGuildEnermy;
            }

            if (_memberInfoDic.ContainsKey(enermyInfo.charId) == true)
            {
                return ePacketCommonResult.CannotGuildMemberToEnermy;
            }

            if (AddGuildEnermy(enermyInfo.charId, enermyInfo.name, enermyInfo._tableId, out var reqRelation) == false)
            {
                return ePacketCommonResult.FailedGuildAction;
            }

            Modify();

            relationList = new List<GuildRelationEntity>()
            {
                reqRelation
            };

            return ePacketCommonResult.Success;
        }

        /// <summary>
        /// 척살유저 삭제 요청
        /// </summary>
        /// <param name="requestCharId"></param>
        /// <param name="enermyInfo"></param>
        /// <param name="relationList"></param>
        /// <returns></returns>
        public ePacketCommonResult ReqGuildRemoveEmermy(long requestCharId, GuildEnermyInfoPacket enermyInfo, out List<GuildRelationEntity> relationList)
        {
            relationList = null;
            if (_memberInfoDic.TryGetValue(requestCharId, out GuildMemberEntity requestEntity) == false)
            {
                return ePacketCommonResult.NotFoundGuildMember;
            }

            if (requestEntity.grade != (int)eGuildMemberGrade.Master &&
                requestEntity.grade != (int)eGuildMemberGrade.ViceMaster)
            {
                return ePacketCommonResult.NoPermissionGuildAction;
            }

            if (IsEnermyToGuild(enermyInfo.charId) == false)
            {
                return ePacketCommonResult.NotGuildEnermy;
            }

            if (RemoveGuildEnermy(enermyInfo.charId, out var reqRelation) == false)
            {
                return ePacketCommonResult.FailedGuildAction;
            }

            Modify();

            relationList = new List<GuildRelationEntity>()
            {
                reqRelation
            };

            return ePacketCommonResult.Success;
        }

        /// <summary>
        /// 길드원 직업 정보 변경
        /// </summary>
        /// <param name="charId"></param>
        /// <param name="changeCharTableId"></param>
        public void ReqGuildMemberClassChange(long charId, int changeCharTableId)
        {
            if (_memberInfoDic.TryGetValue(charId, out GuildMemberEntity entity) == false)
            {
                SLogManager.Instance.ErrorLog($"not found guildMember - charId:{charId}, changeCharTableId:{changeCharTableId}");
                return;
            }

            entity.UpdateCharTableId(changeCharTableId);
        }

        public void Modify(bool needForceSave = false)
        {
            _isModify = true;
            if (needForceSave == true)
            {
                _needForceSave = true;
            }
        }

        public void SetRankingNo(int rankNo)
        {
            _guildEntity.ranking = rankNo;
        }

        public void SaveComplete()
        {
            _isModify = false;
            _needForceSave = false;
        }

        public virtual bool CheckReservedJoin(long charId)
        {
            var now = K2Common.GetDateTime();
            if (_memberInfoDic.Count >= GetMaxMemberCount())
            {
                return false;
            }
            if (_memberInfoDic.Count + _joinReservedDic.Count < GetMaxMemberCount())
            {
                _joinReservedDic[charId] = now;
                return true;
            }
            else
            {
                var expireList = new List<long>();

                foreach (var reservedInfo in _joinReservedDic)
                {
                    if (reservedInfo.Value.AddSeconds(K2Const.GUILD_JOIN_RESERVED_SEC) < now ||
                        reservedInfo.Key == charId)
                    {
                        expireList.Add(reservedInfo.Key);
                    }
                }

                foreach (var expiredCharId in expireList)
                {
                    _joinReservedDic.Remove(expiredCharId);
                }

                if (_memberInfoDic.Count + _joinReservedDic.Count < GetMaxMemberCount())
                {
                    _joinReservedDic[charId] = now;
                    return true;
                }
            }

            return false;
        }

        public bool IsReserveJoinUser(long targetCharId)
        {
            if (_joinReservedDic.ContainsKey(targetCharId))
                return true;

            return false;
        }

        public virtual bool CheckCharacterLogout(long charId)
        {
            var member = GetGuildMemberInfo(charId);
            if (member != null)
            {
                member.SetLogoutTime(K2Common.GetDateTime()) ;
                return true;
            }

            return false;
        }

        public virtual int GetMaxMemberCount()
        {
            if (_cheatMaxMemberCount > 0)
            {
                return _cheatMaxMemberCount;
            }

            var levelData = DataManager.Get<LoaderGuildLevel>().GetGuildLevelData(_guildEntity.level);
            return (levelData == null) ? 0 : levelData.Member;
        }

        public override void UpdateInfo(GuildEntity guild)
        {
            _guildEntity.Copy(guild);
        }

        public override void AddMember(GuildMemberEntity member, bool isNeedDBUpdate = true)
        {
            member.UnPackValues();

            if (_memberInfoDic.ContainsKey(member.charId) == true)
            {
                SLogManager.Instance.ErrorLog($"already exist guildMember - charId:{member.charId}");
            }
            else
            {
                var entity = PoolManager.Instance.GetObject<GuildMemberEntity>();
                entity.Copy(member);

                _memberInfoDic.Add(entity.charId, entity);

                if (isNeedDBUpdate == true)
                {
                    _guildEntity.UpdateGuildMemberCount(_memberInfoDic.Count());
                    Modify();
                }
                else
                {
                    _guildEntity.memberCount = _memberInfoDic.Count();
                }
            }

            _joinReservedDic.Remove(member.charId);
        }

        public override void AddRelation(GuildRelationEntity relation)
        {
            if (relation.IsGuildRelation() == true)
            {
                if (_guildRelationDic.ContainsKey(relation.targetId))
                {
                    SLogManager.Instance.ErrorLog($"already exist guildRelation - targetId:{relation.targetId}");
                }
                else
                {
                    var entity = PoolManager.Instance.GetObject<GuildRelationEntity>();
                    entity.Copy(relation);
                    _guildRelationDic.Add(entity.targetId, entity);
                }
            }
            else if (relation.IsGuildEnermy() == true)
            {
                if (_enermyRelationDic.ContainsKey(relation.targetId))
                {
                    SLogManager.Instance.ErrorLog($"already exist enermyRelation - targetId:{relation.targetId}");
                }
                else
                {
                    var entity = PoolManager.Instance.GetObject<GuildRelationEntity>();
                    entity.Copy(relation);
                    _enermyRelationDic.Add(entity.targetId, entity);
                }
            }
        }

        public override void AddCrestInfo(GuildCrestEntity crest)
        {
            if (_collectedCrestDic.ContainsKey(crest.crestId))
            {
                SLogManager.Instance.ErrorLog($"already exist cresInfo - crestId:{crest.crestId}");
            }
            else
            {
                var entity = PoolManager.Instance.GetObject<GuildCrestEntity>();
                entity.Copy(crest);
                _collectedCrestDic.Add(entity.crestId, entity);
            }
        }

        public virtual void RemoveMember(long charId)
        {
            if (_memberInfoDic.TryGetValue(charId, out GuildMemberEntity entity) == false)
            {
                SLogManager.Instance.ErrorLog($"not exist guildMember - charId:{charId}");
            }
            else
            {
                SLogManager.Instance.InfoLog("RemoveMember() - guildId:{0}, charId:{1}", guildId, entity.charId);

                _memberInfoDic.Remove(charId);
                entity.Dispose();

                _guildEntity.UpdateGuildMemberCount(_memberInfoDic.Count());
                Modify();
            }
        }

        public virtual GuildInfoData GetGuildInfoData()
        {
            var guildInfo = new GuildInfoData();

            guildInfo.guild.Copy(GetGuildInfo());
            guildInfo.skillSlot.Copy(GetGuildSkillSlotEntity());

            var memberList = GetGuildMemberList();
            foreach (var member in memberList)
            {
                guildInfo.memberList.Add(member.Clone());
            }

            var relationList = GetGuildRelationList();
            foreach (var relation in relationList)
            {
                guildInfo.relationList.Add(relation.Clone());
            }

            var enermyList = GetGuildEnermyList();
            foreach (var enermy in enermyList)
            {
                guildInfo.relationList.Add(enermy.Clone());
            }

            var crestList = GetGuildCrestList();
            foreach (var crest in crestList)
            {
                guildInfo.collectedCrestList.Add(crest.Clone());
            }

            var skillList = GetGuildSkillList();
            foreach (var skill in skillList)
            {
                guildInfo.skillList.Add(skill.Clone());
            }

            guildInfo.place.Copy(GetPlace());

            return guildInfo;
        }

        public virtual ICollection<GuildMemberEntity> GetGuildMemberList()
        {
            DateTime now = K2Common.GetDateTime();
            if (_lastResetCheckTime < K2Common.GetLastDailyResetTime(now, Defines.GUILD_RESET_HOUR))
            {
                _lastResetCheckTime = now;
                foreach (var member in _memberInfoDic.Values)
                {
                    member.CheckRefresh(now);
                }
            }

            return _memberInfoDic.Values;
        }

        public virtual GuildMemberEntity GetGuildMemberInfo(long charId)
        {
            if (_memberInfoDic.TryGetValue(charId, out var entity) == true)
                return entity;

            return null;
        }

        public virtual List<GuildRelationEntity> GetGuildRelationList(params eGuildRelationType[] typeList)
        {
            var resultList = new List<GuildRelationEntity>();
            if (typeList.Length > 0)
            {
                foreach (var type in typeList)
                {
                    var entityList = _guildRelationDic.Values.Where(x => (eGuildRelationType)x.relationType == type);
                    resultList.AddRange(entityList);
                }
            }
            else
                resultList.AddRange(_guildRelationDic.Values);

            return resultList;
        }

        public virtual List<GuildRelationEntity> GetGuildEnermyList()
        {
            var resultList = _enermyRelationDic.Values.ToList();
            return resultList;
        }

        public virtual List<GuildCrestEntity> GetGuildCrestList()
        {
            var resultList = _collectedCrestDic.Values.ToList();
            return resultList;
        }

        public virtual List<GuildSkillEntity> GetGuildSkillList()
        {
            var resultList = _guildSkillDic.Values.ToList();
            return resultList;
        }

        public virtual int GetGuildRelationCount(eGuildRelationType type)
        {
            var count = _guildRelationDic.Values.Count(x => (eGuildRelationType)x.relationType == type);
            return count;
        }

        public virtual int GetGuildEnermyCount()
        {
            var count = _enermyRelationDic.Values.Count();
            return count;
        }

        public virtual bool GetGuildRelation(eGuildRelationType relationType, long targetId, out GuildRelationEntity outEntity)
        {
            outEntity = null;
            switch (relationType)
            {
                case eGuildRelationType.Alliance:
                case eGuildRelationType.AllianceInvite:
                case eGuildRelationType.AllianceAccept:
                case eGuildRelationType.Hostile:
                    return _guildRelationDic.TryGetValue(targetId, out outEntity);

                case eGuildRelationType.Enermy:
                    return _enermyRelationDic.TryGetValue(targetId, out outEntity);
            }

            return false;
        }

        public virtual bool IsRelatedToGuild(long targetId, params eGuildRelationType[] typeList)
        {
            if (_guildRelationDic.TryGetValue(targetId, out var relationEntity))
            {
                foreach (var type in typeList)
                {
                    if ((eGuildRelationType)relationEntity.relationType == type)
                        return true;
                }
            }

            return false;
        }

        public virtual bool IsEnermyToGuild(long targetId)
        {
            if (_enermyRelationDic.ContainsKey(targetId) == true)
                return true;

            return false;
        }


        public virtual bool AddGuildRelation(eGuildRelationType relationType, long targetId, string targetName, int targetCrestOrClassId, out GuildRelationEntity outEntity)
        {
            outEntity = null;

            if (_guildRelationDic.ContainsKey(targetId) == false)
            {
                var entity = PoolManager.Instance.GetObject<GuildRelationEntity>();
                entity.Init(guildId, relationType, targetId, targetName, targetCrestOrClassId);
                _guildRelationDic.Add(entity.targetId, entity);
                outEntity = entity.Clone();

                return true;
            }

            return false;
        }

        public virtual bool AddGuildEnermy(long targetId, string targetName, int targetCrestOrClassId, out GuildRelationEntity outEntity)
        {
            outEntity = null;

            if (_enermyRelationDic.ContainsKey(targetId) == true)
            {
                return false;
            }

            var entity = PoolManager.Instance.GetObject<GuildRelationEntity>();
            entity.Init(guildId, eGuildRelationType.Enermy, targetId, targetName, targetCrestOrClassId);
            _enermyRelationDic.Add(entity.targetId, entity);

            outEntity = entity.Clone();

            return true;
        }

        public virtual bool AddGuildCrest(int crestId, out GuildCrestEntity outEntity)
        {
            outEntity = null;

            if (_collectedCrestDic.ContainsKey(crestId) == false)
            {
                var entity = PoolManager.Instance.GetObject<GuildCrestEntity>();
                entity.Init(guildId, crestId);
                _collectedCrestDic.Add(entity.crestId, entity);

                outEntity = entity.Clone();
                return true;
            }

            return false;
        }

        public virtual void RemoveRelationEntity(GuildRelationEntity relation)
        {
            if (relation.IsGuildRelation())
            {
                if (_guildRelationDic.TryGetValue(relation.targetId, out var entity))
                {
                    _guildRelationDic.Remove(relation.targetId);
                    entity.Dispose();
                }
            }
            else if (relation.IsGuildEnermy())
            {
                if (_enermyRelationDic.TryGetValue(relation.targetId, out var entity))
                {
                    _enermyRelationDic.Remove(relation.targetId);
                    entity.Dispose();
                }
            }
        }

        public virtual bool RemoveGuildRelation(long targetId, out GuildRelationEntity outEntity)
        {
            outEntity = null;

            if (_guildRelationDic.TryGetValue(targetId, out var entity) == true)
            {
                entity.Delete();
                outEntity = entity.Clone();
                return true;
            }

            return false;
        }

        public virtual bool RemoveGuildEnermy(long targetId, out GuildRelationEntity outEntity)
        {
            outEntity = null;

            if (_enermyRelationDic.TryGetValue(targetId, out var entity) == false)
            {
                return false;
            }

            entity.Delete();
            outEntity = entity.Clone();
            _enermyRelationDic.Remove(targetId);

            return true;
        }

        public virtual void UpdateInviteExpireTimer(DateTime now)
        {
            foreach (var memberEntity in _memberInfoDic.Values)
            {
                if (memberEntity.grade == (byte)eGuildMemberGrade.Master ||
                    memberEntity.grade == (byte)eGuildMemberGrade.ViceMaster)
                {
                    if (memberEntity.UpdateInviteExpireTimer(now, out var expiredCharNameList))
                    {
                        var targetBBModel = CommunityWorkerManager.Instance.GetUserBlackBoard(memberEntity.charId);
                        if (targetBBModel != null)
                        {
                            // 요청자에게 노티
                            var sendPacket = new SC_ZMQ_GuildInviteResultNotify();
                            sendPacket.result = ePacketCommonResult.ExpireGuildIniviteReq;
                            sendPacket.targetCharNameList = expiredCharNameList;
                            sendPacket.isAccept = false;
                            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, sendPacket, eServerType.Community, targetBBModel.userInfoData.serverId);
                        }
                    }
                }
            }
        }

        public virtual ePacketCommonResult SaveGuildSkillSlot(long requesterId, int skillSlot0, int skillSlot1, int skillSlot2)
        {
            if (false == _memberInfoDic.TryGetValue(requesterId, out var memberEntity))
            {
                return ePacketCommonResult.NotGuildMemberReq;
            }

            if (eGuildMemberGrade.Master != (eGuildMemberGrade)memberEntity.grade)
            {
                return ePacketCommonResult.NoPermissionGuildAction;
            }

            if (false == _guildSkillDic.ContainsKey(skillSlot0) || false == _guildSkillDic.ContainsKey(skillSlot1) || false == _guildSkillDic.ContainsKey(skillSlot2))
            {
                return ePacketCommonResult.NoGuildSkillEntity;
            }

            DateTime now = K2Common.GetDateTime();
            var coolTime = DataManager.Get<LoaderConstantConfig>().GetConstantCofigData("GuildActiveSkillSaveCoolTime").Value1;
            if (now < _guildSkillSlot.lastChangeTime.AddMinutes(coolTime))
            {
                return ePacketCommonResult.CannotSaveSkillSlotCoolTime;
            }

            var maxDailySaveCount = DataManager.Get<LoaderConstantConfig>().GetConstantCofigData("GuildActiveSkillSaveCount").Value1;
            var lastDailyKey = ExtensionUtil.MakeDailyKey(_guildSkillSlot.lastChangeTime, 0, K2Const.ADD_INIT_HOUR);
            var nowDailyKey = ExtensionUtil.MakeDailyKey(now, 0, K2Const.ADD_INIT_HOUR);
            if (lastDailyKey == nowDailyKey && maxDailySaveCount <= _guildSkillSlot.saveCount)
            {
                return ePacketCommonResult.NoMoreSaveSkillSlot;
            }

            _guildSkillSlot.SetGuildSkillSlot(0, skillSlot0);
            _guildSkillSlot.SetGuildSkillSlot(1, skillSlot1);
            _guildSkillSlot.SetGuildSkillSlot(2, skillSlot2);
            _guildSkillSlot.UpdateChangeTime(now, _guildSkillSlot.saveCount + 1);
            Modify();
            return ePacketCommonResult.Success;
        }

        public GuildSkillSlotEntity GetGuildSkillSlotEntity()
        {
            return _guildSkillSlot;
        }

        public virtual bool IsFull()
        {
            return _memberInfoDic.Count >= GetMaxMemberCount();
        }

        public override void AddGuildSkill(GuildSkillEntity skill)
        {
            if (false == _guildSkillDic.ContainsKey(skill.guildSkillId))
            {
                var entity = PoolManager.Instance.GetObject<GuildSkillEntity>();
                entity.Copy(skill);
                _guildSkillDic.Add(entity.guildSkillId, entity);
            }
        }

        public override void UpdateGuildSkillSlot(GuildSkillSlotEntity slot)
        {
            if (slot.IsVaild())
            {
                _guildSkillSlot.Copy(slot);
            }
            else
            {
                _guildSkillSlot.Init(guildId);
            }
        }

        public ePacketCommonResult GuildSkillAdd(int guildSkillId, out GuildSkillEntity skillEntity)
        {
            skillEntity = null;
            if (_guildSkillDic.ContainsKey(guildSkillId))
            {
                return ePacketCommonResult.AlreadyLearnGuildSkill;
            }

            var guildSkillData = DataManager.Get<LoaderGuildSkill>().GetGuildSkill(guildSkillId);
            if (null == guildSkillData)
            {
                return ePacketCommonResult.NoGuildSkillData;
            }

            var enchantList = DataManager.Get<LoaderGuildSkill>().GetGuildSkillEnchantListByGroupId(guildSkillData.GuildSkillEnchantGroupId);
            if (null == enchantList)
            {
                return ePacketCommonResult.NoGuildSkillEnchantData;
            }

            bool isPassiveSkill = LoaderGuildSkill.isPassiveSkill(guildSkillData);

            var entity = PoolManager.Instance.GetObject<GuildSkillEntity>();
            entity.Init(guildId, guildSkillId);

            if (isPassiveSkill)
            {
                entity.EnchantSkill(enchantList[0].EnchantLevel, enchantList[0].SkillBuff);
            }
            else
            {
                entity.EnchantSkill(1, guildSkillData.ActiveSkill);
            }


            _guildSkillDic.Add(guildSkillId, entity);
            Modify();

            skillEntity = new GuildSkillEntity();
            skillEntity.Copy(entity);
            return ePacketCommonResult.Success;
        }

        public ePacketCommonResult GuildSkillEnchant(long charId, int guildSkillId, eCheatRandomStateType rateCheatState, out GuildSkillEntity skillEntity)
        {
            skillEntity = null;

            if (false == _guildSkillDic.TryGetValue(guildSkillId, out var entity))
            {
                return ePacketCommonResult.NoGuildSkillEntity;
            }

            var guildSkillData = DataManager.Get<LoaderGuildSkill>().GetGuildSkill(guildSkillId);
            if (null == guildSkillData)
            {
                return ePacketCommonResult.NoGuildSkillData;
            }

            if (false == LoaderGuildSkill.isPassiveSkill(guildSkillData))
            {
                return ePacketCommonResult.NotEnchantableGuildSkill;
            }

            var now = DateTime.Now;
            if (_lastUpdateGuildSkill > now && _lastUpdateGuildSkillMemberId != charId)
            {
                // 길드 스킬은 여럿이 동시에 못하게 5초 텀으로 방어하지만, 동일인이 연속으로 강화는 가능하다.
                return ePacketCommonResult.CannotGuildSkillEnchantTime;
            }

            _lastUpdateGuildSkill = now.AddSeconds(Defines.GUILD_SKILL_ENCHANT_TERM_SEC);
            _lastUpdateGuildSkillMemberId = charId;

            int enchantGroupId = guildSkillData.GuildSkillEnchantGroupId;
            var enchantList = DataManager.Get<LoaderGuildSkill>().GetGuildSkillEnchantListByGroupId(enchantGroupId);
            var currentEnchantIndex = enchantList.FindIndex(x => x.EnchantLevel == entity.enchantLevel);
            if (-1 == currentEnchantIndex)
            {
                return ePacketCommonResult.NoGuildSkillEnchantData;
            }

            if (enchantList.Count <= currentEnchantIndex + 1)
            {
                return ePacketCommonResult.NoMoreEnchantGuildSKill;
            }

            var nextEnchantData = enchantList[currentEnchantIndex + 1];
            var needPercent = nextEnchantData.EnchantProp;

            var minValue = 0;
            var maxValue = K2Const.MAX_RANDOM_BASE_RATE;
            int randomValue;
            switch (rateCheatState)
            {
                case eCheatRandomStateType.Min:
                    randomValue = minValue;
                    break;
                case eCheatRandomStateType.Max:
                    randomValue = maxValue;
                    break;
                case eCheatRandomStateType.None:
                default:
                    randomValue = RandomManager.Instance.GetNextValue(minValue, maxValue);
                    break;
            }

            if (randomValue < needPercent)
            {
                entity.EnchantSkill(nextEnchantData.EnchantLevel, nextEnchantData.SkillBuff);
                skillEntity = new GuildSkillEntity();
                skillEntity.Copy(entity);

                Modify();
            }

            return ePacketCommonResult.Success;
        }

        // CommunityManager.AddGuildExp 종속
        public void AddExp(long addExp, out bool isLevelUp)
        {
            isLevelUp = false;
            var guildLevelDataManager = DataManager.Get<LoaderGuildLevel>();
            var maxExp = guildLevelDataManager.GetGuildMaxExp();

            if (_guildEntity.exp >= maxExp)
                return;

            while (addExp > 0)
            {
                var needExp = guildLevelDataManager.GetGuildExp(_guildEntity.level);
                var accumExp = guildLevelDataManager.GetGuildAccumExp(_guildEntity.level);
                if (addExp >= needExp)
                {
                    addExp -= needExp;
                    _guildEntity.AddExp(needExp, accumExp, maxExp, out isLevelUp);
                }
                else
                {
                    _guildEntity.AddExp(addExp, accumExp, maxExp, out var levelUp);
                    if (levelUp == true)
                        isLevelUp = true;
                    break;
                }
            }
        }

        #region guildplace
        public override void SetPlace(GuildPlaceEntity place)
        {
            _guildPlace = place.Clone();
        }

        public override void RemovePlace()
        {
            _guildPlace.Dispose();
        }

        public override GuildPlaceEntity GetPlace()
        {
            return _guildPlace;
        }

        public virtual bool CheckPlaceExpand()
        {
            var nextPlaceId = DataManager.Get<LoaderGuildPlace>().GetGuildPlaceId(_guildEntity.level);
            if (_guildPlace.SetPlaceId(nextPlaceId) == false)
                return false;

            return true;
        }

        public GuildPlaceZone GetPlaceZone()
        {
            return _guildPlaceZone;
        }

        // GuildCommunityManager.ClearGuildPlaceZone() 종속
        public bool CheckPlaceZoneClearFlag(eZoneForceChangeType zoneForceChangeType)
        {
            if (_guildPlaceZone.serverId == 0)
                return false;

            return true;
        }

        // GuildCommunityManager.SetGuildPlaceZone() 종속
        public bool SetGuildPlaceZone(GuildPlaceZone placeZone)
        {
            if (_guildPlaceClearFlag != eZoneForceChangeType.None)
                return false;

            if (_guildPlaceZone.EqualValue(placeZone))
            {
                SLogManager.Instance.ErrorLog($"equal values - serverId({placeZone.serverId}), zoneId({placeZone.zoneId}), zoneChannelKey({placeZone.zoneChannelKey})");
            }

            _guildPlaceZone.Set(placeZone);
            return true;
        }

        public eZoneForceChangeType DequeueGuildPlaceClearFlag()
        {
            var flag = _guildPlaceClearFlag;
            _guildPlaceClearFlag = eZoneForceChangeType.None;
            return flag;
        }

        public virtual ePacketCommonResult Rent()
        {
            var data = DataManager.Get<LoaderGuildPlace>().GetGuildPlace(_guildPlace.placeId);
            if (data == null)
                return ePacketCommonResult.Place_NotFoundInfo;

            var result = _guildPlace.Rentable();
            if (result == ePacketCommonResult.Success)
            {
                var now = K2Common.GetDateTime();
                _guildPlace.SetExpireTime(now.AddDays(data.RentDay));
            }
            Modify(true);

            return result;
        }

        public virtual ePacketCommonResult Extension()
        {
            var result = _guildPlace.Extensionable();
            if (result == ePacketCommonResult.Success)
            {
                var data = DataManager.Get<LoaderGuildPlace>().GetGuildPlace(_guildPlace.placeId);
                _guildPlace.AddExpireTime(data.RentDay);
            }
            Modify();

            return result;
        }

        public bool CheckPlaceExpireTime(DateTime now)
        {
            if (_guildPlace.expireTime > now)
                return false;

            if (_guildPlaceZone.serverId <= 0)
                return false;

            return true;
        }

        public void SendGuildPlaceKickOutAllZone(GuildPlaceZone placeZone, eZoneForceChangeType zoneForceChangeType)
        {
            var packet = new SC_ZMQ_GuildPlaceKickOutAllZone();
            packet.guildId = guildId;
            packet.zoneForceChangeType = zoneForceChangeType;
            packet.placeZone.Set(placeZone);
            ServerModule.Instance.Controller.SendZMQ(eServerType.Game, packet, eServerType.Community, _guildPlaceZone.serverId);
        }
        #endregion

        #region guildcrest
        public bool CheckAddGuildCrest()
        {
            return DataManager.Get<LoaderGuildCrest>().HasOpenCrestByLevel(_guildEntity.level);
        }
        #endregion

        #region cheat
        public virtual void SetCheatMaxMemberCount(int count)
        {
            _cheatMaxMemberCount = count;
        }

        public virtual eGuildState GetGuildState()
        {
            return (eGuildState)_guildEntity.state;
        }

        #endregion

    }
}
