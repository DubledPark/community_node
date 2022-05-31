using K2Server.Managers;
using K2Server.Models;
using K2Server.ServerNodes.CommunityNode.Commands;
using K2Server.ServerNodes.CommunityNode.Managers;
using K2Server.Util;
using SharedLib;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace K2Server.ServerNodes.CommunityNode.Thread
{
    public class ThreadModel_GuildCommunityWorker : ThreadModel
    {
        ConcurrentQueue<BaseGuildCommunityCmd> _taskQueues = new ConcurrentQueue<BaseGuildCommunityCmd>();

        public void AddTaskQueue(BaseGuildCommunityCmd cmd)
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
                        if (_taskQueues.TryDequeue(out BaseGuildCommunityCmd cmdResult))
                        {
                            CmdGuildCommunityHandler.ExcuteCmd(cmdResult);
                        }
                    }

                    GuildCommunityManager.Instance.Update(now);

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
