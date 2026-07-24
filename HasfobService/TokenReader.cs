using System.Text;

public class TokenReader
{
    public string GetToken(string filePath)
    {
        try
        {
            string token = File.ReadAllText(filePath, Encoding.UTF8).Trim();
            return token;
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"Write token error: {filePath}", ex);
        }
    }
}