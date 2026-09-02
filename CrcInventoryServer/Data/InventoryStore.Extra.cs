using CrcInventory.Protocol;
using Microsoft.Data.Sqlite;

namespace CrcInventory.Server;

internal sealed partial class InventoryStore
{
    public List<BankRowDto> ListBankAccounts()
    {
        lock (_gate)
        {
            var list = new List<BankRowDto>();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT id, name, bank, last4, notes FROM bank_accounts ORDER BY name COLLATE NOCASE;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new BankRowDto
                {
                    Id = reader.GetInt64(0),
                    Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Bank = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Last4 = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Notes = reader.IsDBNull(4) ? "" : reader.GetString(4)
                });
            }

            return list;
        }
    }

    public long InsertBankAccount(string name, string bank, string last4, string notes)
    {
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO bank_accounts (name, bank, last4, notes, created_at)
                VALUES ($name, $bank, $last4, $notes, $at);
                """;
            cmd.Parameters.AddWithValue("$name", name ?? "");
            cmd.Parameters.AddWithValue("$bank", bank ?? "");
            cmd.Parameters.AddWithValue("$last4", last4 ?? "");
            cmd.Parameters.AddWithValue("$notes", notes ?? "");
            cmd.Parameters.AddWithValue("$at", NowStamp());
            cmd.ExecuteNonQuery();
            cmd.Parameters.Clear();
            cmd.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
    }

    public void UpdateBankAccount(long id, string name, string bank, string last4, string notes)
    {
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                UPDATE bank_accounts
                SET name = $name, bank = $bank, last4 = $last4, notes = $notes
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$name", name ?? "");
            cmd.Parameters.AddWithValue("$bank", bank ?? "");
            cmd.Parameters.AddWithValue("$last4", last4 ?? "");
            cmd.Parameters.AddWithValue("$notes", notes ?? "");
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteBankAccount(long id)
    {
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM bank_accounts WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public BankLinkDto GetBankLiveLink(long id)
    {
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                SELECT COALESCE(plaid_access_token, ''), COALESCE(plaid_item_id, ''),
                       COALESCE(plaid_account_id, ''), COALESCE(plaid_cursor, '')
                FROM bank_accounts WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return new BankLinkDto();
            return new BankLinkDto
            {
                AccessToken = reader.IsDBNull(0) ? "" : reader.GetString(0),
                ItemId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                AccountId = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Cursor = reader.IsDBNull(3) ? "" : reader.GetString(3)
            };
        }
    }

    public void SetBankLiveLink(long id, string accessToken, string itemId, string accountId, string cursor)
    {
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                UPDATE bank_accounts SET
                    plaid_access_token = $token,
                    plaid_item_id = $item,
                    plaid_account_id = $account,
                    plaid_cursor = $cursor
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$token", accessToken ?? "");
            cmd.Parameters.AddWithValue("$item", itemId ?? "");
            cmd.Parameters.AddWithValue("$account", accountId ?? "");
            cmd.Parameters.AddWithValue("$cursor", cursor ?? "");
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void SetBankLiveCursor(long id, string cursor)
    {
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE bank_accounts SET plaid_cursor = $cursor WHERE id = $id;";
            cmd.Parameters.AddWithValue("$cursor", cursor ?? "");
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void SavePdf(string kind, string key, string fileName, byte[] content)
    {
        kind = (kind ?? "").Trim();
        key = (key ?? "").Trim();
        fileName = (fileName ?? "").Trim();
        if (kind.Length == 0 || key.Length == 0 || fileName.Length == 0 || content.Length == 0)
            return;

        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO stored_pdfs (kind, doc_key, file_name, content, stored_at)
                VALUES ($kind, $key, $name, $content, $at)
                ON CONFLICT(kind, doc_key) DO UPDATE SET
                    file_name = excluded.file_name,
                    content = excluded.content,
                    stored_at = excluded.stored_at;
                """;
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$name", fileName);
            var blob = cmd.CreateParameter();
            blob.ParameterName = "$content";
            blob.SqliteType = SqliteType.Blob;
            blob.Value = content;
            cmd.Parameters.Add(blob);
            cmd.Parameters.AddWithValue("$at", NowStamp());
            cmd.ExecuteNonQuery();
        }
    }

    public bool HasPdf(string kind, string key)
    {
        kind = (kind ?? "").Trim();
        key = (key ?? "").Trim();
        if (kind.Length == 0 || key.Length == 0)
            return false;

        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT 1 FROM stored_pdfs WHERE kind = $kind AND doc_key = $key LIMIT 1;";
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$key", key);
            return cmd.ExecuteScalar() != null;
        }
    }

    public PdfDto? TryGetPdf(string kind, string key)
    {
        kind = (kind ?? "").Trim();
        key = (key ?? "").Trim();
        if (kind.Length == 0 || key.Length == 0)
            return null;

        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                SELECT file_name, content FROM stored_pdfs
                WHERE kind = $kind AND (
                    doc_key = $key OR
                    file_name LIKE '%' || $key || '%'
                )
                ORDER BY CASE WHEN doc_key = $key THEN 0 ELSE 1 END, stored_at DESC
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$key", key);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            string name = reader.GetString(0);
            byte[] bytes = reader.IsDBNull(1)
                ? Array.Empty<byte>()
                : reader.GetFieldValue<byte[]>(1);
            if (bytes.Length == 0)
                return null;

            return new PdfDto { FileName = name, Content = bytes };
        }
    }
}
