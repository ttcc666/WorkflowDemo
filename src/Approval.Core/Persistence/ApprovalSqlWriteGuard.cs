using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Approval.Core.Persistence;

/// <summary>Recognizes ordinary DML targets, excluding literals/comments. Not a database permission boundary.</summary>
internal static class ApprovalSqlWriteGuard
{
    private sealed record Token(string Text, bool Word = true)
    { internal bool Is(string word) => Word && Text.Equals(word, StringComparison.OrdinalIgnoreCase); }
    internal static bool WritesProtectedTable(DbContext db, string sql)
    {
        var tables = db.Model.GetEntityTypes().Where(t => ApprovalWriteAuthorization.Protected(t.ClrType))
            .Select(t => (Name: t.GetTableName(), Schema: t.GetSchema())).Where(t => t.Name is not null).ToArray();
        var mysql = db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;
        var tokens = Tokens(sql, mysql);
        bool Protected(string name, string? schema) => tables.Any(t => t.Name!.Equals(name, StringComparison.OrdinalIgnoreCase)
            && (schema is null || schema.Equals(t.Schema ?? db.Model.GetDefaultSchema() ??
                (mysql ? db.Database.GetDbConnection().Database : db.Database.ProviderName?.Contains("SqlServer") == true ? "dbo" : db.Database.ProviderName?.Contains("Npgsql") == true ? "public" : "main"), StringComparison.OrdinalIgnoreCase)));
        (string Name, string? Schema, int End) Target(int at)
        {
            if (at >= tokens.Count) return ("", null, at);
            var name = tokens[at].Text; string? schema = null;
            while (at + 2 < tokens.Count && tokens[at + 1].Text == ".") { schema = name; name = tokens[at + 2].Text; at += 2; }
            return (name, schema, at + 1);
        }
        for (var i = 0; i < tokens.Count; i++)
        {
            var at = -1; var token = tokens[i];
            if (token.Is("UPDATE"))
            {
                at = i + 1;
                if (at < tokens.Count && tokens[at].Is("OR")) at += 2;
            }
            else if (token.Is("INSERT") || token.Is("REPLACE") || token.Is("MERGE"))
            {
                at = i + 1;
                while (at < tokens.Count && !tokens[at].Is("INTO") && tokens[at].Text != ";" && at < i + 4) at++;
                if (at < tokens.Count && tokens[at].Is("INTO")) at++; else at = -1;
            }
            else if (token.Is("DELETE")) { at = i + 1; if (at < tokens.Count && tokens[at].Is("FROM")) at++; }
            else if (token.Is("TRUNCATE")) { at = i + 1; if (at < tokens.Count && tokens[at].Is("TABLE")) at++; }
            if (at < 0) continue;
            if (at < tokens.Count && tokens[at].Is("TOP"))
            {
                at++;
                if (at < tokens.Count && tokens[at].Text == "(")
                { var depth = 0; do { if (tokens[at].Text == "(") depth++; else if (tokens[at].Text == ")") depth--; at++; } while (at < tokens.Count && depth > 0); }
                else at++;
                if (at < tokens.Count && tokens[at].Is("PERCENT")) at++;
                if (at < tokens.Count && tokens[at].Is("FROM")) at++;
            }
            if (at < tokens.Count && tokens[at].Is("ONLY")) at++;
            var target = Target(at);
            if (Protected(target.Name, target.Schema)) return true;
            // SQL Server/MySQL: UPDATE/DELETE alias ... FROM/JOIN schema.Table [AS] alias.
            for (var j = target.End; j < tokens.Count && tokens[j].Text != ";"; j++)
                if (tokens[j].Is("FROM") || tokens[j].Is("JOIN"))
                {
                    var source = Target(j + 1); var alias = source.End;
                    if (alias < tokens.Count && tokens[alias].Is("AS")) alias++;
                    if (alias < tokens.Count && tokens[alias].Text.Equals(target.Name, StringComparison.OrdinalIgnoreCase)
                        && Protected(source.Name, source.Schema)) return true;
                }
        }
        return false;
    }

    private static List<Token> Tokens(string sql, bool mysql = false)
    {
        var result = new List<Token>();
        for (var i = 0; i < sql.Length;)
        {
            if (char.IsWhiteSpace(sql[i])) { i++; continue; }
            if (sql[i] == '$')
            {
                var end = i + 1;
                while (end < sql.Length && (char.IsLetterOrDigit(sql[end]) || sql[end] == '_')) end++;
                if (end < sql.Length && sql[end] == '$')
                {
                    var delimiter = sql[i..(end + 1)]; var close = sql.IndexOf(delimiter, end + 1, StringComparison.Ordinal);
                    i = close < 0 ? sql.Length : close + delimiter.Length; result.Add(new("<literal>", false)); continue;
                }
            }
            if (mysql && sql[i] == '#') { while (i < sql.Length && sql[i] != '\n') i++; continue; }
            if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
            { i += 2; while (i < sql.Length && sql[i] != '\n') i++; continue; }
            if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
            {
                if (mysql && i + 2 < sql.Length && sql[i + 2] == '!')
                {
                    var start = i + 3; while (start < sql.Length && char.IsDigit(sql[start])) start++;
                    var end = sql.IndexOf("*/", start, StringComparison.Ordinal);
                    if (end < 0) end = sql.Length;
                    result.AddRange(Tokens(sql[start..end], mysql)); i = Math.Min(end + 2, sql.Length); continue;
                }
                i += 2; var depth = 1;
                while (i < sql.Length && depth > 0)
                {
                    if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*') { depth++; i += 2; }
                    else if (i + 1 < sql.Length && sql[i] == '*' && sql[i + 1] == '/') { depth--; i += 2; }
                    else i++;
                }
                continue;
            }
            if (sql[i] is '\'' or '"' or '`' or '[')
            {
                var open = sql[i++]; var close = open == '[' ? ']' : open; var value = new StringBuilder();
                while (i < sql.Length)
                {
                    var c = sql[i++];
                    if (c == close)
                    {
                        if (i < sql.Length && sql[i] == close) { value.Append(c); i++; continue; }
                        break;
                    }
                    if (open == '\'' && c == '\\' && i < sql.Length) { i++; continue; }
                    value.Append(c);
                }
                result.Add(new(open == '\'' ? "<literal>" : value.ToString(), false)); continue;
            }
            if (char.IsLetterOrDigit(sql[i]) || sql[i] is '_' or '@' or '$' or '#')
            {
                var start = i++;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] is '_' or '@' or '$' or '#')) i++;
                result.Add(new(sql[start..i])); continue;
            }
            result.Add(new(sql[i++].ToString(), false));
        }
        return result;
    }
}
