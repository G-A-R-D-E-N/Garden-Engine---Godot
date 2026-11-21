using System;
using System.IO;
using System.Text;

namespace OpenCommonwealth.Services.Ui.Character;

public static class AtomicTextFile
{
    // Requires: the directory of path exists; a failure leaves any existing file at path untouched and no temp file behind.
    public static bool Write(string path, string text, bool overwrite)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                stream.Write(new UTF8Encoding(false).GetBytes(text));
                stream.Flush(true);
            }
            File.Move(temp, path, overwrite);
            return true;
        }
        catch (IOException) when (!overwrite && File.Exists(path))
        {
            return false;
        }
        finally
        {
            try { File.Delete(temp); } catch (Exception) { }
        }
    }
}
