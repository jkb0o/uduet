using System;

using SuperSocket.SocketBase.Command;
using SuperSocket.SocketBase.Protocol;

namespace UDuet {

public class REGISTER_MASTER : CommandBase<ProjectSession, StringRequestInfo> {
    public override void ExecuteCommand(ProjectSession session, StringRequestInfo req){
        session.project = ProjectManager.instance.RegisterProject<MasterProject>(req.GetFirstParam());
        session.project.Associate(session);
        Logger.log.Debug("Register master at " + req.GetFirstParam());
    }
}

public class CREATE_SLAVE: CommandBase<ProjectSession, StringRequestInfo> {
    public override void ExecuteCommand(ProjectSession session, StringRequestInfo req){
        Project slave = session.project.GetMaster().CreateSlave();
        Logger.log.Debug("Slave created at " + slave.root);
    }
}

public class REGISTER_SLAVE: CommandBase<ProjectSession, StringRequestInfo> {
    public override void ExecuteCommand(ProjectSession session, StringRequestInfo req){
        session.project = ProjectManager.instance.GetProject(req.GetFirstParam());
        session.project.Associate(session);
        Logger.log.Debug("Register slave at " + req.GetFirstParam());
    }
}

public class CLOSE_SLAVE: CommandBase<ProjectSession, StringRequestInfo> {
    public override void ExecuteCommand(ProjectSession session, StringRequestInfo req){
        Logger.log.Debug("Close slave at " + session.project.root);
        session.project.Clear();
    }
}

public class PASS_OPEN: CommandBase<ProjectSession, StringRequestInfo> {
    public override void ExecuteCommand(ProjectSession session, StringRequestInfo req){
        session.project.GetMaster().session.Send(
            "PASS_OPEN " + req.Parameters[0] + " " + req.Parameters[1]
        );
    }
}

public class STOP: CommandBase<ProjectSession, StringRequestInfo> {
    public override void ExecuteCommand(ProjectSession session, StringRequestInfo req){
        Logger.log.Debug("Recieve stop signal");
        session.Send("DONE");
        Watchdog.instance.stopEvent.Set();
    }
}

public class PING: CommandBase<ProjectSession, StringRequestInfo> {
    public override void ExecuteCommand(ProjectSession session, StringRequestInfo req){
        session.project.Ping();
        session.Send("PONG");
    }
}


}
