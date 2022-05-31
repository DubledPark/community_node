using K2Server.Database.Entities;
using K2Server.FSM;
using K2Server.Managers;
using K2Server.Models;
using K2Server.Packet.Protocol;
using K2Server.ServerNodes.CommunityNode.Models;
using K2Server.Util;
using K2Packet.DataStruct;
using SharedLib;
using SharedLib.Data;
using System.Collections.Generic;
using System.Threading;

namespace K2Server.ServerNodes.CommunityNode.Managers
{
    class ScheduleCommunityManager : Singleton<ScheduleCommunityManager>
    {
        ThreadModel_Schedule _scheduleThread = new ThreadModel_Schedule();
        bool _isLoadingCompleted = false;

        public bool Start()
        {
            var sendPacket = new CS_ZMQ_ScheduleQuery();

            if (false == ServerModule.Instance.Controller.SendZMQ(eServerType.Database, sendPacket, eServerType.Community))
            {
                return false;
            }

            _scheduleThread.Start(0);

            return true;
        }
        
        public void SetScheduleInfo(List<ScheduleEntity> list)
        {
            var scheduleDataList = DataManager.Get<LoaderServerScheduleData>().GetServerScheduleDataList();

            foreach (var scheduleData in scheduleDataList)
            {
                if (scheduleData.ServerType != eServerType.Game)
                    continue;

                var scheduleModel = new ScheduleCommunityModel();
                scheduleModel.Init(scheduleData);

                _scheduleThread.AddSchedule(scheduleModel);
            }

            foreach (var entity in list) 
            {
                var scheduleModel = _scheduleThread.GetSchedule(entity.scheduleId);

                if (scheduleModel != null)
                    scheduleModel.SetScheduleEntity(entity);
                else
                    SLogManager.Instance.ErrorLog($"scheduleModel is null! id : {entity.scheduleId}");
            }

            _scheduleThread.LoadingCompleted();
            _isLoadingCompleted = true;
        }

        // Develop 모드에서만 사용
        public void WaitLoadingComplete()
        {
            var lockObject = new object();
            while (_isLoadingCompleted == false)
            {
                lock (lockObject)
                {
                    Monitor.Wait(lockObject, 10);
                }
            }
        }

        public void ReqScheduleInfo(int fromServerId)
        {
            var sendPacket = new SC_ZMQ_ScheduleInfoForCommunity();
            sendPacket.result = ePacketCommonResult.Success;
            _scheduleThread.GetScheduleList(ref sendPacket.scheduleList);

            var myServerGroupId = ServerModule.Instance.GetMyServerGroupId();

            ServerModule.Instance.Controller.SendZMQ(string.Empty, 0, myServerGroupId, fromServerId, sendPacket);
        }

        public void ReqScheduleControl(int fromServerId, int scheduleId, eScheduleControlType controlType)
        {
            SLogManager.Instance.InfoLog("ReqScheduleControl scheduleId:{0}, controlType:{1}, fromServerId:{2}", scheduleId, controlType, scheduleId);

            var sendPacket = new SC_ZMQ_ScheduleControlForCommunity();
            var scheduleModel = _scheduleThread.GetSchedule(scheduleId);

            if (scheduleModel == null)
            {
                sendPacket.result = ePacketCommonResult.InvalidScheduleId;
                SLogManager.Instance.ErrorLog($"Invalid schedule : {scheduleId}");
            }
            else
            {
                switch (controlType)
                {
                    case eScheduleControlType.CheatStart:
                        if (scheduleModel.GetState() != eScheduleState.Wait)
                        {
                            sendPacket.result = ePacketCommonResult.InvalidScheduleState;
                            SLogManager.Instance.SystemLog("Already start schedule : {0}", scheduleId);
                        }
                        else
                        {
                            sendPacket.result = ePacketCommonResult.Success;
                            scheduleModel.SetNextState(eScheduleState.Start);
                        }
                        break;
                    case eScheduleControlType.CheatEnd:
                        if (scheduleModel.GetState() != eScheduleState.Start)
                        {
                            sendPacket.result = ePacketCommonResult.InvalidScheduleState;
                            SLogManager.Instance.SystemLog("Already end schedule : {0}", scheduleId);
                        }
                        else
                        {
                            sendPacket.result = ePacketCommonResult.Success;
                            scheduleModel.SetNextState(eScheduleState.End);
                        }
                        break;
                    case eScheduleControlType.CheatClear:
                        while(scheduleModel != null)
                        {
                            scheduleModel.SetNextState(eScheduleState.Wait);

                            var nextScheduleId = scheduleModel.GetScheduleData().NextScheduleId;
                            scheduleModel = _scheduleThread.GetSchedule(nextScheduleId);
                        }
                        break;
                    case eScheduleControlType.CheatNext:
                        var nextScheduleModel = scheduleModel;
                        while(nextScheduleModel != null)
                        {
                            if (nextScheduleModel.GetState() == eScheduleState.Start)
                            {
                                nextScheduleModel.SetNextState(eScheduleState.End);
                                break;
                            }

                            var nextScheduleId = nextScheduleModel.GetScheduleData().NextScheduleId;
                            nextScheduleModel = _scheduleThread.GetSchedule(nextScheduleId);
                        }

                        if (nextScheduleModel == null)
                        {
                            scheduleModel.SetNextState(eScheduleState.Start);
                        }

                        break;
                }
            }


            var myServerGroupId = ServerModule.Instance.GetMyServerGroupId();
            ServerModule.Instance.Controller.SendZMQ(string.Empty, 0, myServerGroupId, fromServerId, sendPacket);
        }

        public void ScheduleStart(int scheduleId)
        {
            SLogManager.Instance.InfoLog("ScheduleStart scheduleId:{0}", scheduleId);

            var scheduleModel = _scheduleThread.GetSchedule(scheduleId);
            if (scheduleModel == null)
            {
                SLogManager.Instance.ErrorLog($"Invalid schedule : {scheduleId}");
            }
            else
            {
                scheduleModel.SetNextState(eScheduleState.Start);
            }
        }

        public void SendScheduleNoti(ScheduleCommunityModel scheduleModel, bool isStart = true)
        {
            var scheduleEntity = scheduleModel.GetScheduleEntity();

            var sendPacket = new SC_ZMQ_ScheduleNoti();
            sendPacket.isStart = isStart;
            sendPacket.scheduleInfo.Copy(scheduleEntity);

            var myServerGroupId = ServerModule.Instance.GetMyServerGroupId();
            ServerModule.Instance.Controller.BroadcastZMQ(eServerType.Game, myServerGroupId, sendPacket);
        }

        //public void OnArenaStartAction(ScheduleModel scheduleModel, int arg = 0, DateTime argTime = default(DateTime))
        //{
        //    // 몬스터 침공 시작
        //}

        //public void OnArenaEndAction(ScheduleModel scheduleModel, int arg = 0, DateTime argTime = default(DateTime))
        //{
        //    // 몬스터 침공 종료
        //}
    }
}

