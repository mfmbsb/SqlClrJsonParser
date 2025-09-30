using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SqlTypes;
using Microsoft.SqlServer.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public partial class UserDefinedFunctions
{
    // Ative logs não fatais setando esta flag para true enquanto depura.
    private static readonly bool DEBUG_LOG = false;

    private static void DebugLog(string msg)
    {
        if (!DEBUG_LOG) return;
        try { SqlContext.Pipe.Send("[JsonCLR] " + msg); } catch { /* ignore */ }
    }

    private static JToken ParseRoot(SqlString json)
    {
        if (json.IsNull) throw new ArgumentNullException("json", "JSON de entrada é NULL.");
        var s = json.Value;
        if (string.IsNullOrWhiteSpace(s)) throw new ArgumentException("JSON de entrada está vazio.", "json");

        try
        {
            // Permite root ser objeto OU array
            return JsonConvert.DeserializeObject<JToken>(s);
        }
        catch (JsonException jx)
        {
            throw new ArgumentException("JSON inválido: " + jx.Message, "json");
        }
    }

    private static string SafeToString(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) return null;
        return token.Type == JTokenType.String ? (string)token : token.ToString();
    }

    [SqlFunction(
        DataAccess = DataAccessKind.None,
        SystemDataAccess = SystemDataAccessKind.None,
        IsDeterministic = false // depende do conteúdo do JSON
    )]
    public static SqlString JsonValue(SqlString json, SqlString path)
    {
        if (path.IsNull) throw new ArgumentNullException("path", "Caminho (path) é NULL.");

        var root = ParseRoot(json);

        try
        {
            // Para root array, você normalmente quer $.<...> ou '$[*].foo'.
            // Aqui mantemos o contrato: quem chama define o path.
            var tok = root.SelectToken(path.Value);
            var s = SafeToString(tok);
            return s == null ? SqlString.Null : new SqlString(s);
        }
        catch (Exception ex)
        {
            DebugLog("JsonValue falhou: " + ex.Message);
            // Re-lançar para aparecer erro no SQL (sem 'NULL silencioso').
            throw;
        }
    }

    [SqlFunction(
        DataAccess = DataAccessKind.None,
        SystemDataAccess = SystemDataAccessKind.None,
        IsDeterministic = false
    )]
    public static SqlString JsonArrayValue(SqlString json, SqlInt32 rowindex, SqlString key)
    {
        if (rowindex.IsNull) throw new ArgumentNullException("rowindex", "rowindex é NULL.");
        if (key.IsNull) throw new ArgumentNullException("key", "key é NULL.");

        var root = ParseRoot(json);

        try
        {
            // Aceita root array direto ou busca o primeiro array no root obj.
            JArray arr = root as JArray;
            if (arr == null)
            {
                // Se não for array na raiz, tente encontrar um array em path "$"
                var firstArray = root.SelectToken("$..*") as JArray; // tentativa genérica
                if (firstArray != null) arr = firstArray;
            }
            if (arr == null)
                throw new InvalidOperationException("JSON não possui um array na raiz ou detectável automaticamente para JsonArrayValue.");

            int idx = rowindex.Value;
            if (idx < 0 || idx >= arr.Count)
                throw new IndexOutOfRangeException("rowindex fora do intervalo do array.");

            var item = arr[idx];
            if (item == null) return SqlString.Null;

            var val = item[key.Value];
            var s = SafeToString(val);
            return s == null ? SqlString.Null : new SqlString(s);
        }
        catch (Exception ex)
        {
            DebugLog("JsonArrayValue falhou: " + ex.Message);
            throw;
        }
    }

    // FillRow para TVF
    public static void FillRowFromJson(
        object tokenObj,
        out SqlString path,
        out SqlString value,
        out SqlString type,
        out SqlBoolean hasvalues,
        out SqlInt32 index)
    {
        var tuple = (Tuple<JToken,int>)tokenObj;
        var token = tuple.Item1;
        var i = tuple.Item2;

        path = token == null || token.Path == null ? SqlString.Null : new SqlString(token.Path);
        var v = SafeToString(token);
        value = v == null ? SqlString.Null : new SqlString(v);
        type = token == null ? SqlString.Null : new SqlString(token.Type.ToString());
        hasvalues = token != null && token.HasValues;
        index = new SqlInt32(i);
    }

    [SqlFunction(
        FillRowMethodName = "FillRowFromJson",
        TableDefinition = "[path] nvarchar(4000), [value] nvarchar(max), [type] nvarchar(4000), hasvalues bit, [index] int",
        DataAccess = DataAccessKind.None,
        SystemDataAccess = SystemDataAccessKind.None,
        IsDeterministic = false
    )]
    public static IEnumerable JsonTable(SqlString json, SqlString path)
    {
        if (path.IsNull) throw new ArgumentNullException("path", "Caminho (path) é NULL.");

        var root = ParseRoot(json);

        try
        {
            var list = new List<Tuple<JToken,int>>();
            int i = 0;

            // Seleciona zero, um ou muitos tokens
            IEnumerable<JToken> tokens = root.SelectTokens(path.Value);

            foreach (var token in tokens)
            {
                if (token == null)
                    continue;

                if (token.Type == JTokenType.Object || token.Type == JTokenType.Array)
                {
                    foreach (var child in token.Children<JToken>())
                    {
                        list.Add(Tuple.Create(child, i++));
                    }
                }
                else
                {
                    list.Add(Tuple.Create(token, i++));
                }
            }

            return list;
        }
        catch (Exception ex)
        {
            DebugLog("JsonTable falhou: " + ex.Message);
            throw;
        }
    }
}
