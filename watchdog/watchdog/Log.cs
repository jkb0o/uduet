using System;
using System.IO;

using log4net;
using log4net.Appender;
using log4net.Layout;
using log4net.Config;

namespace UDuet {
    public class Logger {
        public static ILog log;

        public static void InitLogger(){
            // init logging
            var layout = new PatternLayout("%d [%t] %-5p %m%n");
            var appender = new RollingFileAppender {
                File = Watchdog.Location.Log,
                Layout = layout
            };
            layout.ActivateOptions();
            appender.ActivateOptions();
            BasicConfigurator.Configure(appender);
            log = LogManager.GetLogger("uduet");

            AppDomain.CurrentDomain.UnhandledException += (sender, args) => {
                log.Error("Unhandled exception: ",  (Exception)args.ExceptionObject);
            };

        }
    }

}
