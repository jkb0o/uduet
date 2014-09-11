using SuperSocket.Common;
using SuperSocket.SocketBase;
using SuperSocket.SocketBase.Config;
using SuperSocket.SocketBase.Protocol;
using SuperSocket.SocketEngine;

namespace UDuet {

    public class WatchdogServer: AppServer<ProjectSession> {
        protected override bool Setup(IRootConfig rootConfig, IServerConfig config){
            return base.Setup(rootConfig, config);
        }

        protected override void OnStopped(){
            base.OnStopped();
        }
    }

}
