using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace MizanDesktop.Services;

public sealed record AndroidImportReport(
    int Products, int Parties, int Invoices, int InvoiceItems, int Payments,
    int CashTransactions, int StockMovements, int JournalEntries, int JournalLines,
    int Users, int AuditLogs)
{
    public int Total => Products + Parties + Invoices + InvoiceItems + Payments + CashTransactions + StockMovements + JournalEntries + JournalLines + Users + AuditLogs;
    public override string ToString() => $"تم استيراد/تحديث {Total:N0} سجل: منتجات {Products:N0}، أطراف {Parties:N0}، فواتير {Invoices:N0}، بنود {InvoiceItems:N0}، دفعات {Payments:N0}، صندوق {CashTransactions:N0}، مخزون {StockMovements:N0}، قيود {JournalEntries:N0}/{JournalLines:N0}.";
}

/// <summary>Imports the relational Android v3 SQLite database into the WPF operational schema while preserving IDs and references.</summary>
public sealed class AndroidDatabaseImporter
{
    private readonly string _destPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MizanDesktop", "mizan.db");

    public AndroidImportReport Import(string sourceFile)
    {
        if (!File.Exists(sourceFile)) throw new FileNotFoundException("ملف قاعدة Android غير موجود.", sourceFile);
        using var src = new SqliteConnection($"Data Source={sourceFile};Mode=ReadOnly"); src.Open();
        if (!HasTable(src, "tbl_products") || !HasTable(src, "tbl_invoices"))
            throw new InvalidDataException("الملف ليس قاعدة بيانات الميزان Android المتوافقة (tbl_products / tbl_invoices غير موجودة).");

        Directory.CreateDirectory(Path.GetDirectoryName(_destPath)!);
        using var dst = new SqliteConnection($"Data Source={_destPath}"); dst.Open();
        using var tx = dst.BeginTransaction();
        try
        {
            int products = ImportProducts(src, dst, tx);
            int parties = ImportParties(src, dst, tx);
            int invoices = ImportInvoices(src, dst, tx);
            int items = ImportInvoiceItems(src, dst, tx);
            int payments = ImportPayments(src, dst, tx);
            int cash = ImportCash(src, dst, tx);
            int stock = ImportStock(src, dst, tx);
            int entries = ImportJournalEntries(src, dst, tx);
            int lines = ImportJournalLines(src, dst, tx);
            int users = ImportUsers(src, dst, tx);
            int audits = ImportAuditLogs(src, dst, tx);
            RebuildCostLayers(dst, tx);
            tx.Commit();
            return new(products, parties, invoices, items, payments, cash, stock, entries, lines, users, audits);
        }
        catch { tx.Rollback(); throw; }
    }

    static bool HasTable(SqliteConnection c, string name) { using var q=c.CreateCommand();q.CommandText="SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n";q.Parameters.AddWithValue("$n",name);return q.ExecuteScalar()!=null; }
    static string G(string s) { if(Guid.TryParse(s,out var g)) return g.ToString(); var b=MD5.HashData(Encoding.UTF8.GetBytes(s)); return new Guid(b).ToString(); }
    static string S(SqliteDataReader r, string n, string d="") { var i=r.GetOrdinal(n); return r.IsDBNull(i)?d:Convert.ToString(r.GetValue(i),CultureInfo.InvariantCulture)??d; }
    static double D(SqliteDataReader r,string n,double d=0){var i=r.GetOrdinal(n);return r.IsDBNull(i)?d:Convert.ToDouble(r.GetValue(i),CultureInfo.InvariantCulture);}
    static long L(SqliteDataReader r,string n,long d=0){var i=r.GetOrdinal(n);return r.IsDBNull(i)?d:Convert.ToInt64(r.GetValue(i),CultureInfo.InvariantCulture);}
    static string Iso(long ms)=> ms<=0 ? DateTime.Now.ToString("O") : DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("O");
    static int InvoiceType(string x)=>x.ToUpperInvariant() switch{"SALE"=>0,"PURCHASE"=>1,"RETURN_SALE"=>2,"SALE_RETURN"=>2,"RETURN_PURCHASE"=>3,"PURCHASE_RETURN"=>3,"QUOTATION"=>4,_=>0};
    static int PartyType(string x)=>x.ToUpperInvariant()=="SUPPLIER"?1:0;
    static int PaymentType(string x)=>x.ToUpperInvariant() switch{"RECEIPT"=>0,"CUSTOMER_RECEIPT"=>0,"PAYMENT"=>1,"SUPPLIER_PAYMENT"=>1,"INCOME"=>2,"CASH_INCOME"=>2,"EXPENSE"=>3,"CASH_EXPENSE"=>3,_=>0};
    static void Exec(SqliteConnection c,SqliteTransaction tx,string sql,Action<SqliteCommand> fill){using var q=c.CreateCommand();q.Transaction=tx;q.CommandText=sql;fill(q);q.ExecuteNonQuery();}

    static int ImportProducts(SqliteConnection s,SqliteConnection d,SqliteTransaction tx){using var q=s.CreateCommand();q.CommandText="SELECT * FROM tbl_products";using var r=q.ExecuteReader();int n=0;while(r.Read()){var id=G(S(r,"id"));Exec(d,tx,@"INSERT INTO Products(Id,Code,Barcode,Name,Unit,PurchasePrice,SalePrice,Quantity,MinQuantity,IsActive) VALUES($i,'',$b,$n,$u,$pp,$sp,$q,$m,$a) ON CONFLICT(Id) DO UPDATE SET Barcode=$b,Name=$n,Unit=$u,PurchasePrice=$pp,SalePrice=$sp,Quantity=$q,MinQuantity=$m,IsActive=$a",c=>{c.Parameters.AddWithValue("$i",id);c.Parameters.AddWithValue("$b",S(r,"barcode"));c.Parameters.AddWithValue("$n",S(r,"name"));c.Parameters.AddWithValue("$u",S(r,"unit","قطعة"));c.Parameters.AddWithValue("$pp",D(r,"cost_price"));c.Parameters.AddWithValue("$sp",D(r,"sale_price"));c.Parameters.AddWithValue("$q",D(r,"quantity"));c.Parameters.AddWithValue("$m",D(r,"min_stock_alert"));c.Parameters.AddWithValue("$a",L(r,"is_archived")==0?1:0);});n++;}return n;}
    static int ImportParties(SqliteConnection s,SqliteConnection d,SqliteTransaction tx){using var q=s.CreateCommand();q.CommandText="SELECT * FROM tbl_parties";using var r=q.ExecuteReader();int n=0;while(r.Read()){var id=G(S(r,"id"));Exec(d,tx,@"INSERT INTO Parties(Id,Name,Phone,Address,Type,Balance) VALUES($i,$n,$p,$a,$t,$b) ON CONFLICT(Id) DO UPDATE SET Name=$n,Phone=$p,Address=$a,Type=$t,Balance=$b",c=>{c.Parameters.AddWithValue("$i",id);c.Parameters.AddWithValue("$n",S(r,"name"));c.Parameters.AddWithValue("$p",S(r,"phone"));c.Parameters.AddWithValue("$a",S(r,"address"));c.Parameters.AddWithValue("$t",PartyType(S(r,"type")));c.Parameters.AddWithValue("$b",D(r,"balance"));});n++;}return n;}
    static int ImportInvoices(SqliteConnection s,SqliteConnection d,SqliteTransaction tx){using var q=s.CreateCommand();q.CommandText="SELECT * FROM tbl_invoices";using var r=q.ExecuteReader();int n=0;while(r.Read()){var id=G(S(r,"id"));var party=S(r,"party_id");Exec(d,tx,"DELETE FROM InvoiceLines WHERE InvoiceId=$i",c=>c.Parameters.AddWithValue("$i",id));Exec(d,tx,@"INSERT INTO Invoices(Id,Number,Date,Type,PartyId,PartyName,Total,IsCredit,Discount,FeesAmount,FeesNote,PaidAmount,PaymentMethod,Currency,Notes,Status) VALUES($i,$no,$d,$t,$p,$pn,$tot,$cr,$di,$f,$fn,$pa,$pm,$cu,$nt,$st) ON CONFLICT(Id) DO UPDATE SET Number=$no,Date=$d,Type=$t,PartyId=$p,PartyName=$pn,Total=$tot,IsCredit=$cr,Discount=$di,FeesAmount=$f,FeesNote=$fn,PaidAmount=$pa,PaymentMethod=$pm,Currency=$cu,Notes=$nt,Status=$st",c=>{var total=D(r,"net_amount");var paid=D(r,"paid_amount");c.Parameters.AddWithValue("$i",id);c.Parameters.AddWithValue("$no",S(r,"number"));c.Parameters.AddWithValue("$d",Iso(L(r,"date")));c.Parameters.AddWithValue("$t",InvoiceType(S(r,"type")));c.Parameters.AddWithValue("$p",string.IsNullOrWhiteSpace(party)?"":G(party));c.Parameters.AddWithValue("$pn",S(r,"party_name"));c.Parameters.AddWithValue("$tot",total);c.Parameters.AddWithValue("$cr",D(r,"remaining_amount")>0.0001?1:0);c.Parameters.AddWithValue("$di",D(r,"discount"));c.Parameters.AddWithValue("$f",D(r,"fees_amount"));c.Parameters.AddWithValue("$fn",S(r,"fees_note"));c.Parameters.AddWithValue("$pa",paid);c.Parameters.AddWithValue("$pm",S(r,"payment_method","CASH"));c.Parameters.AddWithValue("$cu",S(r,"currency","SYP"));c.Parameters.AddWithValue("$nt",S(r,"notes"));c.Parameters.AddWithValue("$st",L(r,"is_cancelled")!=0?"CANCELLED":S(r,"status","CLOSED"));});n++;}return n;}
    static int ImportInvoiceItems(SqliteConnection s,SqliteConnection d,SqliteTransaction tx){using var q=s.CreateCommand();q.CommandText="SELECT * FROM tbl_invoice_items";using var r=q.ExecuteReader();int n=0;while(r.Read()){var pid=S(r,"product_id");Exec(d,tx,"INSERT INTO InvoiceLines(InvoiceId,ProductId,ProductName,Quantity,UnitPrice,Total,UnitCost) VALUES($i,$p,$n,$q,$u,$t,$c)",c=>{c.Parameters.AddWithValue("$i",G(S(r,"invoice_id")));c.Parameters.AddWithValue("$p",string.IsNullOrWhiteSpace(pid)?G("missing:"+S(r,"id")):G(pid));c.Parameters.AddWithValue("$n",S(r,"product_name"));c.Parameters.AddWithValue("$q",D(r,"quantity"));c.Parameters.AddWithValue("$u",D(r,"unit_price"));c.Parameters.AddWithValue("$t",D(r,"total_price"));c.Parameters.AddWithValue("$c",D(r,"unit_cost"));});n++;}return n;}
    static int ImportPayments(SqliteConnection s,SqliteConnection d,SqliteTransaction tx){using var q=s.CreateCommand();q.CommandText="SELECT * FROM tbl_payments";using var r=q.ExecuteReader();int n=0;while(r.Read()){var party=S(r,"party_id");var inv=S(r,"invoice_id");Exec(d,tx,@"INSERT INTO Payments(Id,Date,Type,PartyId,PartyName,InvoiceId,Amount,PaymentMethod,Description,Currency,FundId,Cancelled) VALUES($i,$d,$t,$p,$pn,$v,$a,$m,$x,$c,$f,$z) ON CONFLICT(Id) DO UPDATE SET Date=$d,Type=$t,PartyId=$p,PartyName=$pn,InvoiceId=$v,Amount=$a,PaymentMethod=$m,Currency=$c,FundId=$f,Cancelled=$z",c=>{c.Parameters.AddWithValue("$i",G(S(r,"id")));c.Parameters.AddWithValue("$d",Iso(L(r,"date")));c.Parameters.AddWithValue("$t",PaymentType(S(r,"type")));c.Parameters.AddWithValue("$p",string.IsNullOrWhiteSpace(party)?"":G(party));c.Parameters.AddWithValue("$pn",S(r,"party_name"));c.Parameters.AddWithValue("$v",string.IsNullOrWhiteSpace(inv)?"":G(inv));c.Parameters.AddWithValue("$a",D(r,"amount"));c.Parameters.AddWithValue("$m",S(r,"payment_method","CASH"));c.Parameters.AddWithValue("$x","مستورد من Android");c.Parameters.AddWithValue("$c",S(r,"currency","SYP"));c.Parameters.AddWithValue("$f",S(r,"fund_id",S(r,"currency","SYP")=="USD"?"MAIN-USD":"MAIN-SYP"));c.Parameters.AddWithValue("$z",L(r,"is_cancelled"));});n++;}return n;}
    static int ImportCash(SqliteConnection s,SqliteConnection d,SqliteTransaction tx){using var q=s.CreateCommand();q.CommandText="SELECT * FROM tbl_cash_transactions";using var r=q.ExecuteReader();int n=0;while(r.Read()){var party=S(r,"party_id");var inv=S(r,"invoice_id");Exec(d,tx,@"INSERT INTO CashTransactions(Id,Date,FundId,Type,Amount,Currency,Description,ReferenceId,PartyId,IsCancelled) VALUES($i,$d,$f,$t,$a,$c,$x,$r,$p,$z) ON CONFLICT(Id) DO UPDATE SET Date=$d,FundId=$f,Type=$t,Amount=$a,Currency=$c,Description=$x,ReferenceId=$r,PartyId=$p,IsCancelled=$z",c=>{c.Parameters.AddWithValue("$i",G(S(r,"id")));c.Parameters.AddWithValue("$d",Iso(L(r,"date")));c.Parameters.AddWithValue("$f",S(r,"fund_id","MAIN-SYP"));c.Parameters.AddWithValue("$t",S(r,"type","IN"));c.Parameters.AddWithValue("$a",D(r,"amount"));c.Parameters.AddWithValue("$c",S(r,"currency","SYP"));c.Parameters.AddWithValue("$x",S(r,"description"));c.Parameters.AddWithValue("$r",string.IsNullOrWhiteSpace(inv)?"":G(inv));c.Parameters.AddWithValue("$p",string.IsNullOrWhiteSpace(party)?"":G(party));c.Parameters.AddWithValue("$z",L(r,"is_cancelled"));});n++;}return n;}
    static int ImportStock(SqliteConnection s,SqliteConnection d,SqliteTransaction tx){using var q=s.CreateCommand();q.CommandText="SELECT * FROM tbl_stock_movements";using var r=q.ExecuteReader();int n=0;while(r.Read()){Exec(d,tx,@"INSERT INTO StockMovements(Id,ProductId,ProductName,Type,QuantityChange,QuantityBefore,QuantityAfter,UnitCost,UnitPrice,ReferenceId,Date,Notes) VALUES($i,$p,$n,$t,$q,$b,$a,$c,$u,$r,$d,$x) ON CONFLICT(Id) DO UPDATE SET ProductId=$p,ProductName=$n,Type=$t,QuantityChange=$q,QuantityBefore=$b,QuantityAfter=$a,UnitCost=$c,UnitPrice=$u,ReferenceId=$r,Date=$d,Notes=$x",c=>{c.Parameters.AddWithValue("$i",G(S(r,"id")));c.Parameters.AddWithValue("$p",G(S(r,"product_id")));c.Parameters.AddWithValue("$n",S(r,"product_name"));c.Parameters.AddWithValue("$t",S(r,"type"));c.Parameters.AddWithValue("$q",D(r,"quantity_change"));c.Parameters.AddWithValue("$b",D(r,"quantity_before"));c.Parameters.AddWithValue("$a",D(r,"quantity_after"));c.Parameters.AddWithValue("$c",D(r,"unit_cost"));c.Parameters.AddWithValue("$u",D(r,"unit_price"));c.Parameters.AddWithValue("$r",S(r,"reference_id") is var rr && rr!=""?G(rr):"");c.Parameters.AddWithValue("$d",Iso(L(r,"date")));c.Parameters.AddWithValue("$x",S(r,"notes"));});n++;}return n;}
    static int ImportJournalEntries(SqliteConnection s,SqliteConnection d,SqliteTransaction tx){using var q=s.CreateCommand();q.CommandText="SELECT * FROM tbl_journal_entries";using var r=q.ExecuteReader();int n=0;while(r.Read()){var rid=S(r,"reference_id");var eid=G(S(r,"id"));Exec(d,tx,"DELETE FROM JournalLines WHERE EntryId=$e",c=>c.Parameters.AddWithValue("$e",eid));Exec(d,tx,@"INSERT INTO JournalEntries(Id,Date,Description,Reference) VALUES($i,$d,$x,$r) ON CONFLICT(Id) DO UPDATE SET Date=$d,Description=$x,Reference=$r",c=>{c.Parameters.AddWithValue("$i",eid);c.Parameters.AddWithValue("$d",Iso(L(r,"date")));c.Parameters.AddWithValue("$x",S(r,"description"));c.Parameters.AddWithValue("$r",rid);});n++;}return n;}
    static int ImportJournalLines(SqliteConnection s,SqliteConnection d,SqliteTransaction tx){using var q=s.CreateCommand();q.CommandText="SELECT * FROM tbl_journal_lines";using var r=q.ExecuteReader();int n=0;while(r.Read()){Exec(d,tx,"INSERT INTO JournalLines(EntryId,Account,Debit,Credit) VALUES($e,$a,$d,$c)",c=>{c.Parameters.AddWithValue("$e",G(S(r,"entry_id")));c.Parameters.AddWithValue("$a",S(r,"account_name",S(r,"account_code",S(r,"account_id"))));c.Parameters.AddWithValue("$d",D(r,"debit"));c.Parameters.AddWithValue("$c",D(r,"credit"));});n++;}return n;}
    static int ImportUsers(SqliteConnection s,SqliteConnection d,SqliteTransaction tx){if(!HasTable(s,"tbl_users"))return 0;using var q=s.CreateCommand();q.CommandText="SELECT * FROM tbl_users";using var r=q.ExecuteReader();int n=0;while(r.Read()){Exec(d,tx,@"INSERT INTO Users(Id,Name,Role,PinHash,IsActive,CreatedAt) VALUES($i,$n,$r,$h,$a,$d) ON CONFLICT(Id) DO UPDATE SET Name=$n,Role=$r,PinHash=$h,IsActive=$a",c=>{c.Parameters.AddWithValue("$i",G(S(r,"id")));c.Parameters.AddWithValue("$n",S(r,"name","مستخدم Android"));c.Parameters.AddWithValue("$r",S(r,"role","CASHIER"));c.Parameters.AddWithValue("$h",S(r,"pin_hash",S(r,"pin")));c.Parameters.AddWithValue("$a",L(r,"is_active",1));c.Parameters.AddWithValue("$d",DateTime.Now.ToString("O"));});n++;}return n;}
    static int ImportAuditLogs(SqliteConnection s,SqliteConnection d,SqliteTransaction tx){if(!HasTable(s,"tbl_audit_logs"))return 0;using var q=s.CreateCommand();q.CommandText="SELECT * FROM tbl_audit_logs";using var r=q.ExecuteReader();int n=0;while(r.Read()){Exec(d,tx,@"INSERT OR IGNORE INTO AuditLogs(Id,Action,EntityType,EntityId,Description,PerformedBy,Timestamp) VALUES($i,$a,$t,$e,$x,$u,$d)",c=>{c.Parameters.AddWithValue("$i",G(S(r,"id")));c.Parameters.AddWithValue("$a",S(r,"action"));c.Parameters.AddWithValue("$t",S(r,"entity_type"));c.Parameters.AddWithValue("$e",S(r,"entity_id"));c.Parameters.AddWithValue("$x",S(r,"description"));c.Parameters.AddWithValue("$u",S(r,"performed_by"));c.Parameters.AddWithValue("$d",Iso(L(r,"timestamp",L(r,"date"))));});n++;}return n;}
    /// <summary>
    /// Reconstruct FIFO layers by replaying imported invoices chronologically.
    /// Purchases and sales returns add layers; sales and purchase returns consume them.
    /// Missing historical purchase data is recorded as a zero-quantity audit marker rather
    /// than silently overstating the available stock layers.
    /// </summary>
    static void RebuildCostLayers(SqliteConnection d, SqliteTransaction tx)
    {
        Exec(d, tx, "DELETE FROM CostLayers", _ => { });

        using var q = d.CreateCommand();
        q.Transaction = tx;
        q.CommandText = """
            SELECT Id,Number,Date,Type,Currency
            FROM Invoices
            WHERE Status <> 'CANCELLED' AND Type <> 4
            ORDER BY Date ASC, Id ASC
            """;

        var invoices = new List<(string Id,string Number,DateTime Date,int Type,string Currency)>();
        using (var r = q.ExecuteReader())
        {
            while (r.Read())
            {
                invoices.Add((
                    r.GetString(0), r.GetString(1), DateTime.Parse(r.GetString(2)),
                    r.GetInt32(3), r.GetString(4)));
            }
        }

        foreach (var inv in invoices)
        {
            using var lq = d.CreateCommand();
            lq.Transaction = tx;
            lq.CommandText = """
                SELECT ProductId,ProductName,Quantity,UnitCost
                FROM InvoiceLines
                WHERE InvoiceId=$id
                ORDER BY Id
                """;
            lq.Parameters.AddWithValue("$id", inv.Id);

            using var lr = lq.ExecuteReader();
            while (lr.Read())
            {
                var productId = lr.GetString(0);
                var productName = lr.GetString(1);
                var qty = (decimal)lr.GetDouble(2);
                var unitCost = (decimal)lr.GetDouble(3);
                if (qty <= 0 || string.IsNullOrWhiteSpace(productId)) continue;

                // Purchase / sale return: inventory enters the FIFO pool.
                if (inv.Type is 1 or 2)
                {
                    Exec(d, tx, """
                        INSERT INTO CostLayers(ProductId,CreatedAt,ReferenceId,ReferenceNumber,
                                               QuantityIn,QuantityRemaining,UnitCost,Currency)
                        VALUES($p,$d,$r,$n,$q,$q,$c,$cur)
                        """, c =>
                    {
                        c.Parameters.AddWithValue("$p", productId);
                        c.Parameters.AddWithValue("$d", inv.Date.ToString("O"));
                        c.Parameters.AddWithValue("$r", inv.Id);
                        c.Parameters.AddWithValue("$n", inv.Number);
                        c.Parameters.AddWithValue("$q", (double)qty);
                        c.Parameters.AddWithValue("$c", (double)Math.Max(0, unitCost));
                        c.Parameters.AddWithValue("$cur", string.IsNullOrWhiteSpace(inv.Currency) ? "SYP" : inv.Currency);
                    });
                    continue;
                }

                // Sale / purchase return: consume oldest available layers.
                if (inv.Type is 0 or 3)
                {
                    var left = qty;
                    while (left > 0)
                    {
                        long layerId = 0;
                        decimal available = 0;

                        using (var cq = d.CreateCommand())
                        {
                            cq.Transaction = tx;
                            cq.CommandText = """
                                SELECT Id,QuantityRemaining
                                FROM CostLayers
                                WHERE ProductId=$p AND QuantityRemaining>0
                                ORDER BY Id
                                LIMIT 1
                                """;
                            cq.Parameters.AddWithValue("$p", productId);
                            using var cr = cq.ExecuteReader();
                            if (cr.Read())
                            {
                                layerId = cr.GetInt64(0);
                                available = (decimal)cr.GetDouble(1);
                            }
                        }

                        if (layerId == 0)
                        {
                            // The source database can legitimately contain an older sale
                            // while its historical purchases are absent from the export.
                            // Preserve that fact explicitly.
                            Exec(d, tx, """
                                INSERT INTO CostLayers(ProductId,CreatedAt,ReferenceId,ReferenceNumber,
                                                       QuantityIn,QuantityRemaining,UnitCost,Currency)
                                VALUES($p,$d,$r,$n,0,0,$c,$cur)
                                """, c =>
                            {
                                c.Parameters.AddWithValue("$p", productId);
                                c.Parameters.AddWithValue("$d", inv.Date.ToString("O"));
                                c.Parameters.AddWithValue("$r", inv.Id);
                                c.Parameters.AddWithValue("$n", $"{inv.Number} / {productName}");
                                c.Parameters.AddWithValue("$c", (double)Math.Max(0, unitCost));
                                c.Parameters.AddWithValue("$cur", string.IsNullOrWhiteSpace(inv.Currency) ? "SYP" : inv.Currency);
                            });
                            break;
                        }

                        var take = Math.Min(left, available);
                        Exec(d, tx,
                            "UPDATE CostLayers SET QuantityRemaining=QuantityRemaining-$q WHERE Id=$id",
                            c =>
                            {
                                c.Parameters.AddWithValue("$q", (double)take);
                                c.Parameters.AddWithValue("$id", layerId);
                            });
                        left -= take;
                    }
                }
            }
        }
    }

}
