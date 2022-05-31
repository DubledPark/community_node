using K2Server.Managers;
using K2Server.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace K2Server.ServerNodes.CommunityNode.Commands
{
    public class BaseGuildCommunityCmd
    {
        public long uId { get; protected set; }
        public int fromServerId { get; protected set; }
        public int fromServerGroupId { get; protected set; }
        public string sourceSessionId { get; protected set; }
        public virtual eGuildCommunityCmdType cmdId { get { return eGuildCommunityCmdType.eNone; } }

        public void Init(long uId, int fromServerId, int fromServerGroupId, string sessionId)
        {
            this.uId = uId;
            this.fromServerId = fromServerId;
            this.fromServerGroupId = fromServerGroupId;
            this.sourceSessionId = sessionId;
        }
    }

    public static class CmdGuildCommunityHandler
    {
        public delegate void CmdMethodHandler(BaseGuildCommunityCmd baseCmd);

        static Dictionary<eGuildCommunityCmdType, CmdMethodHandler> _cmdHandlerDic = new Dictionary<eGuildCommunityCmdType, CmdMethodHandler>();

        public static void CmdBind()
        {
            var query = from method in typeof(GuildCommunityCmdLayer).GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public)
                        from attr in method.GetCustomAttributes(typeof(GuildCommunityCmdAttribute), false)
                        where attr is GuildCommunityCmdAttribute
                        select new { method, attr };
            
            foreach (var elem in query)
            {
                var cmdAtt = elem.attr as GuildCommunityCmdAttribute;

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

        public static void ExcuteCmd(BaseGuildCommunityCmd cmdBase)
        {
            var handler = (CmdMethodHandler)_cmdHandlerDic[cmdBase.cmdId];
            handler?.Invoke(cmdBase);
        }
    }
}
