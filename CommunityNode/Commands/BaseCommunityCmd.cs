using K2Server.Managers;
using K2Server.Util;
using SharedLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace K2Server.ServerNodes.CommunityNode.Commands
{
    public class BaseCommunityCmd
    {
        public long uId { get; protected set; }
        public int fromServerId { get; protected set; }
        public int fromServerGroupId { get; protected set; }
        public string sourceSessionId { get; protected set; }
        public virtual eCommunityCmdType cmdId { get { return eCommunityCmdType.eNone; } }

        public DateTime createTime = K2Common.GetDateTime();

        public void Init(long uId, int fromServerId, int fromServerGroupId, string sessionId)
        {
            this.uId = uId;
            this.fromServerId = fromServerId;
            this.fromServerGroupId = fromServerGroupId;
            this.sourceSessionId = sessionId;
        }
    }

    public static class CmdCommunityHandler
    {
        public delegate void CmdMethodHandler(BaseCommunityCmd baseCmd);

        static Dictionary<eCommunityCmdType, CmdMethodHandler> _cmdHandlerDic = new Dictionary<eCommunityCmdType, CmdMethodHandler>();

        public static void CmdBind()
        {
            var query = from method in typeof(CommunityCmdLayer).GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public)
                        from attr in method.GetCustomAttributes(typeof(CommunityCmdAttribute), false)
                        where attr is CommunityCmdAttribute
                        select new { method, attr };

            foreach (var elem in query)
            {
                var cmdAtt = elem.attr as CommunityCmdAttribute;

                if (!_cmdHandlerDic.ContainsKey(cmdAtt.eCmdId))
                {
                    _cmdHandlerDic[cmdAtt.eCmdId] = (CmdMethodHandler)Delegate.CreateDelegate(typeof(CmdMethodHandler), elem.method);
                }
                else
                {
                    SLogManager.Instance.ErrorLog($"Failed bind => eCmdId:{cmdAtt.eCmdId}, type:{cmdAtt.cmdType}");
                }
            }

        }

        public static void ExcuteCmd(BaseCommunityCmd cmdBase)
        {
            var handler = (CmdMethodHandler)_cmdHandlerDic[cmdBase.cmdId];
            handler?.Invoke(cmdBase);
        }
    }
}
