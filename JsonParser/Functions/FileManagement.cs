using System;
using System.Data.SqlTypes;
using System.IO;
using Microsoft.SqlServer.Server;

public partial class StoredProcedures
{
    // Stored Procedure to write content to a file
    [Microsoft.SqlServer.Server.SqlProcedure]
    public static void WriteToFile(SqlString filePath, SqlString content)
    {
        // Convert SqlString to .NET string
        string path = filePath.Value;
        string textContent = content.Value;

        // Write the content to the file
        File.WriteAllText(path, textContent);
    }

    // Stored Procedure to read content from a file
    [Microsoft.SqlServer.Server.SqlProcedure]
    public static void ReadFromFile(SqlString filePath, out SqlString fileContent)
    {
        // Convert SqlString to .NET string
        string path = filePath.Value;

        // Check if file exists
        if (File.Exists(path))
        {
            // Read the content of the file
            string content = File.ReadAllText(path);
            fileContent = new SqlString(content);
        }
        else
        {
            throw new FileNotFoundException("The specified file does not exist.");
        }
    }
}
