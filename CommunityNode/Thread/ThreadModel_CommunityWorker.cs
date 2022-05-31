using K2Server.Managers;
using K2Server.Models;
using K2Server.ServerNodes.CommunityNode.Commands;
using K2Server.ServerNodes.CommunityNode.Managers;
using K2Server.ServerNodes.GameNode.Managers;
using K2Server.Util;
using SharedLib;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace K2Server.ServerNodes.CommunityNode.Thread
{
    public class ThreadModel_CommunityWorker : ThreadModel
    {
        ConcurrentQueue<BaseCommunityCmd> _taskQueues = new ConcurrentQueue<BaseCommunityCmd>();

        public void AddTaskQueue(BaseCommunityCmd cmd)
        {
            _taskQueues.Enqueue(cmd);
            //SetEvent();
        }

        protected override void ProcessUpdate()
        {
            while (isDispose == false)
            {
                try
                {
                    //WaitOne();

                    var frameStartTime = DateTime.Now;
                    var now = K2Common.GetDateTime();
                    while (_taskQueues.IsEmpty == false)
                    {
                        if (_taskQueues.TryDequeue(out BaseCommunityCmd cmdResult))
                        {
                            
                            var delayTime = (now - cmdResult.createTime).TotalMilliseconds;
                            if (1000 < delayTime)
                            {
                                SLogManager.Instance.ErrorLog($"CommunityWorker Cmd Dequeue 경고! Type : {cmdResult.cmdId}, Delay : {delayTime}");
                            }

                            CmdCommunityHandler.ExcuteCmd(cmdResult);

                            var processTime = (DateTime.Now - now).TotalMilliseconds;
                            if ( 100 < processTime)
                            {
                                SLogManager.Instance.ErrorLog($"CommunityWorker Cmd 처리 경고! Type : {cmdResult.cmdId}, processTime : {processTime}");
                            }

                        }
                    }

                    CommunityWorkerManager.Instance.Check();
                    FriendCommunityManager.Instance.Check(now);
                    AdminMessageManager.Instance.Check();
                    HiddenBossCommunityManager.Instance.HiddenBossUpdate(now);
                    PartyCommunityManager.Instance.Check(now);
                    
                    //GuildCommunityManager.Instance.Update(startTime);
                    //ObservationCommunityManager.Instance.Check();

                    var processingTime = (long)(DateTime.Now - frameStartTime).TotalMilliseconds;
                    lock (_lockObject)
                    {
                        // Same as a Thread.Sleep(), but it will wake up if signalled by Monitor.Pulse()
                        if (Defines.TICK_COMMUNITY_WORK_UPDATE - processingTime > 0)
                        {
                            Monitor.Wait(_lockObject, TimeSpan.FromMilliseconds(Defines.TICK_COMMUNITY_WORK_UPDATE - processingTime));
                        }
                        else
                        {
                            Monitor.Wait(_lockObject, 1);
                        }

                        // After 500 milli seconds have passed or we have been signalled, check the Cancellation Token
                        if (_cancellationTokenSource.Token.IsCancellationRequested)
                            return;
                    }

                }
                catch (Exception e)
                {
                    SLogManager.Instance.ExceptionLog(e.ToString());
                }
            }
        }
    }
}
