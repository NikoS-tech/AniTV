using System.IO;

namespace AniTV;

public static class StateFile
{
    public static string Quarantine(string path)
    {
        var destination=path+".damaged-"+Guid.NewGuid().ToString("N");
        File.Copy(path,destination,false);
        return destination;
    }

    public static void Write(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                using(var writer=new StreamWriter(stream,leaveOpen:true)) { writer.Write(json); writer.Flush(); }
                stream.Flush(true);
            }
            if(File.Exists(path)) File.Replace(temporary,path,path+".bak");
            else File.Move(temporary,path);
        }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
}
