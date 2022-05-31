using K2Packet.DataStruct;
using K2Server.Database.Entities;
using K2Server.Managers;
using K2Server.Packet.Protocol;
using RRCommon.Enum;
using SharedLib;
using SharedLib.Data;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace K2Server.ServerNodes.CommunityNode.Managers
{
    // 운영툴에서 관리되는 이벤트를 처리하기 위한 manager
    class EventCommunityManager : Singleton<EventCommunityManager>
    {
        // <idx, eventEntity>
        Dictionary<long, EventEntity> _eventDic = new Dictionary<long, EventEntity>();

        // 이벤트 미션
        ConcurrentDictionary<int, EventHubPageInfo> EventMissions { get; set; } = new ConcurrentDictionary<int, EventHubPageInfo>();

        public bool Init()
        {
            var serverController = ServerModule.Instance.Controller;

            // 핫타임 이벤트
            var sendPacket = new CS_ZMQ_EventLoadQuery();
            if (false == serverController.SendZMQ(eServerType.Database, sendPacket, eServerType.Community))
            {
                SLogManager.Instance.ErrorLog("Send CS_ZMQ_EventLoadQuery() Error.");
                return false;
            }

            // 이벤트 미션
            var eventMissionPacket = new CS_ZMQ_EventMissionLoadQuery();
            if (false == serverController.SendZMQ(eServerType.Database, eventMissionPacket, eServerType.Community))
            {
                SLogManager.Instance.ErrorLog("Send CS_ZMQ_EventMissionLoadQuery() Error.");
                return false;
            }

            return true;
        }

        #region 핫타임 이벤트
        public void EventLoad(List<EventEntity> eventList)
        {
            var serverGroupId = ServerModule.Instance.GetMyServerGroupId();

            foreach (var data in eventList)
            {
                if (data.groupId.Contains(serverGroupId.ToString()))
                {
                    var entity = PoolManager.Instance.GetObject<EventEntity>();
                    entity.Copy(data);
                    _eventDic.Add(data.idx, entity);
                }
            }

            var packet = new SC_ZMQ_SendEvent();
            packet.eventList.AddRange(_eventDic.Values);
            ServerModule.Instance.Controller.BroadcastZMQ(eServerType.Game, packet);

            SLogManager.Instance.InfoLog("!!!!!Event DB Load Complete!!!!! EventCount({0})", _eventDic.Count());
        }

        /// <summary>
        /// 변경된 이벤트 정보
        /// </summary>
        /// <param name="eventEntity"></param>
        public void EventAdd(EventEntity eventEntity)
        {
            if (_eventDic.ContainsKey(eventEntity.idx) == false && _eventDic.TryGetValue(eventEntity.idx, out var entity) == false)
            {
                _eventDic.Add(eventEntity.idx, eventEntity.Clone());
            }

            var packet = new SC_ZMQ_SendEvent();
            packet.eventList.AddRange(_eventDic.Values);
            ServerModule.Instance.Controller.BroadcastZMQ(eServerType.Game, packet);

            SLogManager.Instance.InfoLog("!!!!!Event Add!!!!! EventCount({0})", _eventDic.Count());
        }

        /// <summary>
        /// 변경된 이벤트 정보
        /// </summary>
        /// <param name="eventEntity"></param>
        public void EventUpdate(EventEntity eventEntity)
        {
            if (_eventDic.ContainsKey(eventEntity.idx) && _eventDic.TryGetValue(eventEntity.idx, out var entity)){
                _eventDic[eventEntity.idx].Copy(eventEntity);
            }
            else
            {
                _eventDic.Add(eventEntity.idx, eventEntity.Clone());
            }

            var packet = new SC_ZMQ_SendEvent();
            packet.eventList.AddRange(_eventDic.Values);
            ServerModule.Instance.Controller.BroadcastZMQ(eServerType.Game, packet);

            SLogManager.Instance.InfoLog("!!!!!Event Update!!!!! EventCount({0})", _eventDic.Count());
        }

        /// <summary>
        /// 이벤트 정보 삭제
        /// </summary>
        /// <param name="eventIdx"></param>
        public void EventDelete(long eventIdx)
        {
            if (_eventDic.ContainsKey(eventIdx) && _eventDic.TryGetValue(eventIdx, out var entity))
            {
                _eventDic.Remove(eventIdx);
            }

            var packet = new SC_ZMQ_SendEvent();
            packet.eventList.AddRange(_eventDic.Values);
            ServerModule.Instance.Controller.BroadcastZMQ(eServerType.Game, packet);

            SLogManager.Instance.InfoLog("!!!!!Event Delete!!!!! EventCount({0})", _eventDic.Count());
        }
        #endregion

        #region 이벤트 미션
        /// <summary>
        /// 진행 가능 이벤트 미션 로드
        /// Community => Game
        /// </summary>
        /// <param name="eventMissionList"></param>
        public void EventMissionLoad(List<EventMissionEntity> eventMissionList)
        {
            var now = K2Common.GetDateTime();

            var serverGroupId = ServerModule.Instance.GetMyServerGroupId();

            var eventHubData = DataManager.Get<LoaderEventHubPage>().GetEventDatas();
            foreach (var _event in eventHubData)
            {
                // DB : event DB 의 event_mission 테이블에 데이터가 있을 경우 종료된 이벤트 미션
                var eventMission = eventMissionList.FirstOrDefault(e => e.eventId == _event.EventId && e.groupId == serverGroupId);
                if (eventMission != null)
                {
                    continue;
                }

                // 기간 만료된 이벤트 미션
                if (_event.EventStartDate.AddDays(_event.EventPeriod) < now)
                {
                    continue;
                }

                // 진행 예정인 이벤트 미션 Add
                EventMissions.TryAdd(_event.EventId, _event);
            }

            // 진행중인 이벤트 미션을 게임 노드에 전달
            var sendPacket = new SC_ZMQ_SendEventMission();
            foreach (var _event in EventMissions.Values)
            {
                var eventHubInfo = new EventHubPageInfo
                {
                    Id = _event.Id,
                    EventId = _event.EventId,
                    EventType = _event.EventType,
                    CategoryTopNumber = _event.CategoryTopNumber,
                    EventStartDate = _event.EventStartDate,
                    EventPeriod = _event.EventPeriod,
                    BG = _event.BG,
                    EventName = _event.EventName,
                };

                sendPacket.eventMissionList.Add(eventHubInfo);
            }

            ServerModule.Instance.Controller.BroadcastZMQ(eServerType.Game, sendPacket);

            SLogManager.Instance.InfoLog("Event Mission Data load complete. EventCount({0})", EventMissions.Count());
        }

        /// <summary>
        /// 이벤트 미션 재시작
        /// Community => Game
        /// </summary>
        /// <param name="eventId"></param>
        /// <param name="eventType"></param>
        /// <returns></returns>
        public ePacketCommonResult EventMissionUpdate(int eventId, eEventType eventType, int serverGroupId)
        {
            var result = ePacketCommonResult.Success;

            if (ServerModule.Instance.GetMyServerGroupId() != serverGroupId)
            {
                SLogManager.Instance.ErrorLog("EventMissionUpdate()", "invalid serverGroupId : eventId({0}), eventType({1}), serverGroupId({2})",
                    eventId, eventType, serverGroupId);
                return ePacketCommonResult.InvalidSelectServerGroupId;
            }

            if (EventMissions.TryGetValue(eventId, out var _) == true)
            {
                SLogManager.Instance.ErrorLog("EventMissionUpdate()", "invalid EventMission : eventId({0}), eventType({1}), serverGroupId({2})",
                    eventId, eventType, serverGroupId);
                return ePacketCommonResult.InvalidEventMission;
            }

            var eventHubData = DataManager.Get<LoaderEventHubPage>().GetEventPageDataByEventId(eventId);
            if (eventHubData == null)
            {
                SLogManager.Instance.ErrorLog("EventMissionUpdate()", "not found LoaderEventHubPage : eventId({0}), eventType({1}), serverGroupId({2})",
                    eventId, eventType, serverGroupId);
                return ePacketCommonResult.InvaildTableData;
            }

            var now = K2Common.GetDateTime();
            if (eventHubData.EventStartDate.AddDays(eventHubData.EventPeriod) < now)
            {
                SLogManager.Instance.ErrorLog("EventMissionUpdate()", "already ended LoaderEventHubPage : eventId({0}), eventType({1}), serverGroupId({2})",
                    eventId, eventType, serverGroupId);
                return ePacketCommonResult.InvalidEventType;
            }

            int beforCount = EventMissions.Count();

            EventMissions.TryAdd(eventHubData.EventId, eventHubData);

            var sendPacket = new SC_ZMQ_SendEventMissionUpdate
            {
                eventId = eventId,
                eventType = eventType,
                eventMissionUpdateType = eEventMissionUpdateType.Update,
            };

            ServerModule.Instance.Controller.BroadcastZMQ(eServerType.Game, sendPacket);

            SLogManager.Instance.InfoLog("Event Mission Data update complete. beforCount({0}), afterCount({1})", beforCount, EventMissions.Count());

            return result;
        }

        /// <summary>
        /// 이벤트 미션 강제 종료
        /// Community => Game
        /// </summary>
        /// <param name="eventId"></param>
        /// <param name="eventType"></param>
        /// <returns></returns>
        public ePacketCommonResult EventMissionRemove(int eventId, eEventType eventType, int serverGroupId)
        {
            var result = ePacketCommonResult.Success;

            if (ServerModule.Instance.GetMyServerGroupId() != serverGroupId)
            {
                SLogManager.Instance.ErrorLog("EventMissionRemove()", "invalid serverGroupId : eventId({0}), eventType({1}), serverGroupId({2})",
                    eventId, eventType, serverGroupId);
                return ePacketCommonResult.InvalidSelectServerGroupId;
            }

            if (EventMissions.TryGetValue(eventId, out var _) == false)
            {
                SLogManager.Instance.ErrorLog("EventMissionRemove()", "not found EventMission : eventId({0}), eventType({1}), serverGroupId({2})",
                    eventId, eventType, serverGroupId);
                return ePacketCommonResult.NotFoundEventMission;
            }

            var eventHubData = DataManager.Get<LoaderEventHubPage>().GetEventPageDataByEventId(eventId);
            if (eventHubData == null)
            {
                SLogManager.Instance.ErrorLog("EventMissionRemove()", "not found LoaderEventHubPage : eventId({0}), eventType({1}), serverGroupId({2})",
                    eventId, eventType, serverGroupId);
                return ePacketCommonResult.InvaildTableData;
            }

            int beforCount = EventMissions.Count();

            EventMissions.TryRemove(eventId, out var _);

            var sendPacket = new SC_ZMQ_SendEventMissionUpdate
            {
                eventId = eventId,
                eventType = eventType,
                eventMissionUpdateType = eEventMissionUpdateType.Remove,
            };

            ServerModule.Instance.Controller.BroadcastZMQ(eServerType.Game, sendPacket);

            SLogManager.Instance.InfoLog("Event Mission Data remove complete. beforCount({0}), afterCount({1})", beforCount, EventMissions.Count());

            return result;
        }
        #endregion
    }
}
