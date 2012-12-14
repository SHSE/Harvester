using System;
using System.IO;
using System.Text;
using System.Xml;
using Harvester.Core.Processes;

namespace Harvester.Core.Messaging.Parsers
{
    internal class Log4JParserForRemoteMessage : IParseMessages
    {
        private readonly XmlNamespaceManager xmlNamespaceManager;
        private readonly XmlParserContext xmlParserContext;

        public Log4JParserForRemoteMessage(
            IRetrieveProcesses processRetriever,
            IHaveExtendedProperties extendedProperties)
        {
            Verify.NotNull(processRetriever, "processRetriever");
            Verify.NotNull(extendedProperties, "extendedProperties");

            this.xmlNamespaceManager = new XmlNamespaceManager(new NameTable());
            this.xmlNamespaceManager.AddNamespace("log4j", "http://logging.apache.org/log4j/");
            this.xmlParserContext = new XmlParserContext(null, this.xmlNamespaceManager, null, XmlSpace.None);
        }

        public SystemEvent Parse(IMessage message)
        {
            Verify.NotNull(message, "message");

            var document = new XmlDocument();

            using (var reader = new XmlTextReader(
                message.Message ?? string.Empty, XmlNodeType.Element, this.xmlParserContext))
                document.Load(reader);

            var process = this.GetProcess(document);

            return new SystemEvent {
                Level = this.GetLevel(document),
                ProcessName = process.Name,
                ProcessId = process.Id,
                Timestamp = message.Timestamp,
                Thread = this.GetThread(document),
                Username = this.GetUsername(document),
                Source = this.GetSource(document),
                Message = this.GetMessage(document).Trim() + this.GetException(document),
                RawMessage = new Lazy<string>(() => FormatRawMessage(document)),
            };
        }

        private string GetException(XmlNode document)
        {
            var exception = this.QuerySingleValue(document, "./log4j:event/log4j:properties/log4j:data[@name='exception']/@value");

            if (!string.IsNullOrEmpty(exception))
                return Environment.NewLine + " -> " + exception;

            return string.Empty;
        }

        public Boolean CanParseMessage(String message)
        {
            return message != null &&
                   message.StartsWith("<log4j:event ") &&
                   message.EndsWith("</log4j:event>") &&
                   message.Contains("<log4j:data name=\"log4jmachinename\" ");
        }

        private SystemEventLevel GetLevel(XmlNode document)
        {
            var level = this.QuerySingleValue(document, "./log4j:event/@level");
            switch (level.ToLowerInvariant())
            {
                case "fatal":
                    return SystemEventLevel.Fatal;
                case "error":
                    return SystemEventLevel.Error;
                case "warn":
                    return SystemEventLevel.Warning;
                case "info":
                    return SystemEventLevel.Information;
                case "debug":
                    return SystemEventLevel.Debug;
                default:
                    return SystemEventLevel.Trace;
            }
        }

        private String GetThread(XmlNode document)
        {
            return this.QuerySingleValue(document, "./log4j:event/@thread");
        }

        private String GetUsername(XmlNode document)
        {
            return this.QuerySingleValue(document, "./log4j:event/log4j:properties/log4j:data[@name='username']/@value");
        }

        private String GetMessage(XmlNode document)
        {
            return this.QuerySingleValue(document, "./log4j:event/log4j:message/text()");
        }

        private string GetSource(XmlNode document)
        {
            return
                this.GetMachineName(document) +
                '\\' +
                this.QuerySingleValue(document, "./log4j:event/@logger");
        }

        private string GetMachineName(XmlNode document)
        {
            return this.QuerySingleValue(
                document, "./log4j:event/log4j:properties/log4j:data[@name='log4jmachinename']/@value");
        }

        private RemoteProcess GetProcess(XmlNode document)
        {
            var appInfo = this.QuerySingleValue(
                document, "./log4j:event/log4j:properties/log4j:data[@name='log4japp']/@value");

            if (string.IsNullOrEmpty(appInfo))
                return new RemoteProcess();

            appInfo = appInfo.TrimEnd(')');

            var i = appInfo.LastIndexOf('(');

            if (i == -1)
                return new RemoteProcess();

            return new RemoteProcess {
                Id = int.Parse(appInfo.Substring(i + 1)),
                Name = appInfo.Substring(0, i)
            };
        }

        private String QuerySingleValue(XmlNode node, String xPath)
        {
            var result = node.SelectSingleNode(xPath, this.xmlNamespaceManager);

            return result == null ? String.Empty : result.Value ?? String.Empty;
        }

        private static String FormatRawMessage(XmlNode document)
        {
            var sb = new StringBuilder();

            using (var stringWriter = new StringWriter(sb))
            using (var xmlTextWriter = new XmlTextWriter(stringWriter))
            {
                xmlTextWriter.Formatting = Formatting.Indented;

                document.WriteTo(xmlTextWriter);
            }

            return sb.ToString();
        }

        private class RemoteProcess : IProcess
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool HasExited { get; set; }
            public DateTime? ExitTime { get; set; }
        }
    }
}