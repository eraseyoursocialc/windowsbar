using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace WindowsBar.Helper
{
    [DataContract]
    internal sealed class Command
    {
        [DataMember(Name = "cmd")] public string Cmd { get; set; }
        [DataMember(Name = "position")] public double Position { get; set; }
        [DataMember(Name = "paths")] public string[] Paths { get; set; }
    }

    [DataContract]
    internal sealed class MediaMessage
    {
        [DataMember(Name = "type")] public string Type { get; set; } = "media";
        [DataMember(Name = "hasSession")] public bool HasSession { get; set; }
        [DataMember(Name = "title")] public string Title { get; set; } = "";
        [DataMember(Name = "artist")] public string Artist { get; set; } = "";
        [DataMember(Name = "playing")] public bool Playing { get; set; }
        [DataMember(Name = "position")] public double Position { get; set; }
        [DataMember(Name = "duration")] public double Duration { get; set; }
        /// <summary>When the playing app last reported the position (Unix ms), so the app can count forward.</summary>
        [DataMember(Name = "updated")] public double Updated { get; set; }
        [DataMember(Name = "canSeek")] public bool CanSeek { get; set; }
    }

    [DataContract]
    internal sealed class ArtMessage
    {
        [DataMember(Name = "type")] public string Type { get; set; } = "art";
        [DataMember(Name = "data")] public string Data { get; set; } = "";
    }

    [DataContract]
    internal sealed class FlagMessage
    {
        [DataMember(Name = "type")] public string Type { get; set; }
        [DataMember(Name = "value")] public bool Value { get; set; }
    }

    [DataContract]
    internal sealed class ErrorMessage
    {
        [DataMember(Name = "type")] public string Type { get; set; } = "error";
        [DataMember(Name = "message")] public string Message { get; set; }
    }

    internal static class Json
    {
        public static T Read<T>(string line) where T : class
        {
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                    return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
            catch
            {
                return null;
            }
        }

        public static string Write<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    /// <summary>Writes one JSON message per line to stdout.</summary>
    internal sealed class Output
    {
        private readonly object _gate = new object();
        private readonly StreamWriter _writer =
            new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

        public void Send<T>(T message)
        {
            var line = Json.Write(message);
            lock (_gate)
            {
                try
                {
                    _writer.WriteLine(line);
                }
                catch (IOException)
                {
                    // The app has closed; the stdin reader will shut us down.
                }
            }
        }

        public void Error(string message) => Send(new ErrorMessage { Message = message });
    }
}
