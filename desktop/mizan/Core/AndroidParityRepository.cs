using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Data;
using Microsoft.Data.Sqlite;

namespace MizanDesktop.Core;

public sealed class AndroidParityRepository
{
    private readonly string _connectionString;

    public AndroidParityRepository()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MizanDesktop");
        Directory.CreateDirectory(root);
        _connectionString = $"Data Source={Path.Combine(root, "mizan.db")}";
        using var db = Open();
        AndroidParitySchema.Ensure(db);
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        return db;
    }

    public long ProductCount() => Scalar("SELECT COUNT(*) FROM tbl_products");
    public long PartyCount() => Scalar("SELECT COUNT(*) FROM tbl_parties");
    public long InvoiceCount() => Scalar("SELECT COUNT(*) FROM tbl_invoices");
    public long StockMovementCount() => Scalar("SELECT COUNT(*) FROM tbl_stock_movements");
    public long JournalEntryCount() => Scalar("SELECT COUNT(*) FROM tbl_journal_entries");

    private long Scalar(string sql)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public string BuildAndroidAppDataJson(string deviceId, string deviceName)
    {
        using var db = Open();
        MirrorOperationalToParity(db);
        var root = new JsonObject
        {
            ["parties"] = ReadParties(db), ["products"] = ReadProducts(db), ["invoices"] = ReadInvoices(db),
            ["cashTransactions"] = ReadCash(db), ["payments"] = ReadPayments(db), ["stockMovements"] = ReadStock(db),
            ["journalEntries"] = ReadJournals(db), ["accounts"] = ReadAccounts(db), ["auditLogs"] = ReadAudits(db),
            ["users"] = ReadUsers(db), ["settings"] = new JsonObject { ["storeName"] = "الميزان", ["deviceName"] = deviceName, ["currency"] = "SYP", ["defaultCurrency"] = "SYP" },
            ["syncDevices"] = new JsonArray(), ["funds"] = new JsonArray(), ["specialAccountTxs"] = new JsonArray(), ["inventoryAudits"] = new JsonArray(),
            ["nextSaleNumber"] = 1, ["nextPurchaseNumber"] = 1, ["nextReturnSaleNumber"] = 1, ["nextReturnPurchaseNumber"] = 1,
            ["nextReceiptVoucherNumber"] = 1, ["nextPaymentVoucherNumber"] = 1, ["nextAuditNumber"] = 1, ["nextTransferNumber"] = 1,
            ["nextExchangeNumber"] = 1, ["nextQuotationNumber"] = 1, ["nextJournalEntryNumber"] = 1, ["nextPaymentNumber"] = 1,
            ["license"] = null
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    public SyncImportCounts ImportAndroidAppDataJson(string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        using var db = Open(); using var tx = db.BeginTransaction();
        try
        {
            var p = UpsertProducts(db, tx, root); var parties = UpsertParties(db, tx, root); var inv = UpsertInvoices(db, tx, root);
            var pay = UpsertPayments(db, tx, root); var cash = UpsertCash(db, tx, root); var stock = UpsertStock(db, tx, root); var journals = UpsertJournals(db, tx, root);
            UpsertAccounts(db, tx, root); UpsertAudits(db, tx, root); UpsertUsers(db, tx, root); UpsertLicense(db, tx, root);
            tx.Commit();
            MirrorParityToOperational(db);
            return new SyncImportCounts(p, parties, inv, pay, cash, stock, journals);
        }
        catch { tx.Rollback(); throw; }
    }


    private void MirrorOperationalToParity(SqliteConnection db)
    {
        using var tx=db.BeginTransaction();
        try {
            // Delete children before parents because parity schema enables foreign_keys.
            Exec(db,tx,"DELETE FROM tbl_cash_transactions",_=>{});
            Exec(db,tx,"DELETE FROM tbl_stock_movements",_=>{});
            Exec(db,tx,"DELETE FROM tbl_payments",_=>{});
            Exec(db,tx,"DELETE FROM tbl_journal_lines",_=>{});
            Exec(db,tx,"DELETE FROM tbl_journal_entries",_=>{});
            Exec(db,tx,"DELETE FROM tbl_invoice_items",_=>{});
            Exec(db,tx,"DELETE FROM tbl_invoices",_=>{});
            Exec(db,tx,"DELETE FROM tbl_audit_logs",_=>{});
            Exec(db,tx,"DELETE FROM tbl_accounts",_=>{});
            Exec(db,tx,"DELETE FROM tbl_parties",_=>{});
            Exec(db,tx,"DELETE FROM tbl_products",_=>{});
            CopyProducts(db,tx); CopyParties(db,tx); CopyInvoices(db,tx); CopyPayments(db,tx);
            CopyCashOperational(db,tx); CopyStockOperational(db,tx); CopyAccountsOperational(db,tx); CopyJournalsOperational(db,tx);
            tx.Commit();
        } catch { tx.Rollback(); throw; }
    }
    private static void CopyProducts(SqliteConnection db,SqliteTransaction tx){using var r=Query(db,tx,"SELECT Id,Barcode,Name,Unit,PurchasePrice,SalePrice,Quantity,MinQuantity FROM Products");while(r.Read())Exec(db,tx,"INSERT OR REPLACE INTO tbl_products(id,barcode,name,unit,cost_price,sale_price,quantity,min_stock_alert,default_currency) VALUES($i,$b,$n,$u,$c,$s,$q,$m,'SYP')",c=>{c.Parameters.AddWithValue("$i",r.GetString(0));c.Parameters.AddWithValue("$b",r.IsDBNull(1)?"":r.GetString(1));c.Parameters.AddWithValue("$n",r.GetString(2));c.Parameters.AddWithValue("$u",r.IsDBNull(3)?"قطعة":r.GetString(3));c.Parameters.AddWithValue("$c",r.GetDouble(4));c.Parameters.AddWithValue("$s",r.GetDouble(5));c.Parameters.AddWithValue("$q",r.GetDouble(6));c.Parameters.AddWithValue("$m",r.GetDouble(7));});}
    private static void CopyParties(SqliteConnection db,SqliteTransaction tx){using var r=Query(db,tx,"SELECT Id,Name,Phone,Address,Type,Balance FROM Parties");while(r.Read())Exec(db,tx,"INSERT OR REPLACE INTO tbl_parties(id,name,phone,address,type,balance,default_currency,currency) VALUES($i,$n,$p,$a,$t,$b,'SYP','SYP')",c=>{c.Parameters.AddWithValue("$i",r.GetString(0));c.Parameters.AddWithValue("$n",r.GetString(1));c.Parameters.AddWithValue("$p",r.IsDBNull(2)?"":r.GetString(2));c.Parameters.AddWithValue("$a",r.IsDBNull(3)?"":r.GetString(3));c.Parameters.AddWithValue("$t",r.GetInt32(4)==1?"SUPPLIER":"CUSTOMER");c.Parameters.AddWithValue("$b",r.GetDouble(5));});}
    private static void CopyInvoices(SqliteConnection db,SqliteTransaction tx){using var r=Query(db,tx,"SELECT Id,Number,Date,Type,PartyId,PartyName,Total,Discount,FeesAmount,FeesNote,PaidAmount,PaymentMethod,Currency,Notes,Status FROM Invoices");while(r.Read()){var id=r.GetString(0);Exec(db,tx,"INSERT OR REPLACE INTO tbl_invoices(id,number,type,party_id,party_name,net_amount,discount,fees_amount,fees_note,paid_amount,remaining_amount,currency,date,is_cancelled,status,notes,payment_method,fund_id) VALUES($i,$n,$t,$p,$pn,$tot,$di,$f,$fn,$pa,$rem,$cu,$d,$can,$st,$nt,$pm,'main_cash')",c=>{var total=r.GetDouble(6);var paid=r.GetDouble(10);c.Parameters.AddWithValue("$i",id);c.Parameters.AddWithValue("$n",r.GetString(1));c.Parameters.AddWithValue("$t",r.GetInt32(3) switch{1=>"PURCHASE",2=>"RETURN_SALE",3=>"RETURN_PURCHASE",4=>"QUOTATION",_=>"SALE"});c.Parameters.AddWithValue("$p",r.IsDBNull(4)?"":r.GetString(4));c.Parameters.AddWithValue("$pn",r.IsDBNull(5)?"":r.GetString(5));c.Parameters.AddWithValue("$tot",total);c.Parameters.AddWithValue("$di",r.GetDouble(7));c.Parameters.AddWithValue("$f",r.GetDouble(8));c.Parameters.AddWithValue("$fn",r.IsDBNull(9)?"":r.GetString(9));c.Parameters.AddWithValue("$pa",paid);c.Parameters.AddWithValue("$rem",Math.Max(0,total-paid));c.Parameters.AddWithValue("$cu",r.IsDBNull(12)?"SYP":r.GetString(12));c.Parameters.AddWithValue("$d",DateTimeOffset.TryParse(r.GetString(2),out var dt)?dt.ToUnixTimeMilliseconds():DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());c.Parameters.AddWithValue("$can",r.IsDBNull(14)||r.GetString(14)!="CANCELLED"?0:1);c.Parameters.AddWithValue("$st",r.IsDBNull(14)?"CLOSED":r.GetString(14));c.Parameters.AddWithValue("$nt",r.IsDBNull(13)?"":r.GetString(13));c.Parameters.AddWithValue("$pm",r.IsDBNull(11)?"CASH":r.GetString(11));});Exec(db,tx,"DELETE FROM tbl_invoice_items WHERE invoice_id=$i",c=>c.Parameters.AddWithValue("$i",id));using var ir=Query(db,tx,"SELECT Id,ProductId,ProductName,Quantity,UnitPrice,UnitCost,Total, 'قطعة' FROM InvoiceLines WHERE InvoiceId=$i",("$i",id));while(ir.Read())Exec(db,tx,"INSERT OR REPLACE INTO tbl_invoice_items(id,invoice_id,product_id,product_name,quantity,unit_price,unit_cost,total_price,unit) VALUES($i,$v,$p,$n,$q,$u,$c,$t,$un)",c=>{c.Parameters.AddWithValue("$i",ir.GetInt64(0).ToString());c.Parameters.AddWithValue("$v",id);c.Parameters.AddWithValue("$p",ir.IsDBNull(1)?"":ir.GetString(1));c.Parameters.AddWithValue("$n",ir.GetString(2));c.Parameters.AddWithValue("$q",ir.GetDouble(3));c.Parameters.AddWithValue("$u",ir.GetDouble(4));c.Parameters.AddWithValue("$c",ir.GetDouble(5));c.Parameters.AddWithValue("$t",ir.GetDouble(6));c.Parameters.AddWithValue("$un",ir.GetString(7));});}}
    private static void CopyPayments(SqliteConnection db,SqliteTransaction tx){using var r=Query(db,tx,"SELECT Id,Date,Type,PartyId,PartyName,Amount,PaymentMethod,Currency,FundId,InvoiceId,Cancelled FROM Payments");while(r.Read())Exec(db,tx,"INSERT OR REPLACE INTO tbl_payments(id,payment_number,invoice_id,party_id,party_name,fund_id,amount,currency,type,payment_method,date,is_cancelled) VALUES($i,0,$v,$p,$pn,$f,$a,$c,$t,$m,$d,$z)",c=>{c.Parameters.AddWithValue("$i",r.GetString(0));c.Parameters.AddWithValue("$v",r.IsDBNull(9)?"":r.GetString(9));c.Parameters.AddWithValue("$p",r.IsDBNull(3)?"":r.GetString(3));c.Parameters.AddWithValue("$pn",r.IsDBNull(4)?"":r.GetString(4));c.Parameters.AddWithValue("$f",r.IsDBNull(8)?"main_cash":r.GetString(8));c.Parameters.AddWithValue("$a",r.GetDouble(5));c.Parameters.AddWithValue("$c",r.IsDBNull(7)?"SYP":r.GetString(7));c.Parameters.AddWithValue("$t",r.GetInt32(2) switch{1=>"PARTY_PAYMENT",2=>"EXPENSE",_=>"PARTY_RECEIPT"});c.Parameters.AddWithValue("$m",r.IsDBNull(6)?"CASH":r.GetString(6));c.Parameters.AddWithValue("$d",DateTimeOffset.TryParse(r.GetString(1),out var dt)?dt.ToUnixTimeMilliseconds():DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());c.Parameters.AddWithValue("$z",r.GetInt32(10));});}
    private static void CopyCashOperational(SqliteConnection db,SqliteTransaction tx){using var r=Query(db,tx,"SELECT Id,Date,FundId,Type,Amount,Currency,Description,PartyId,IsCancelled FROM CashTransactions");while(r.Read())Exec(db,tx,"INSERT OR REPLACE INTO tbl_cash_transactions(id,tx_number,fund_id,type,amount,currency,description,party_id,is_cancelled,date) VALUES($i,'',$f,$t,$a,$c,$d,$p,$z,$dt)",c=>{c.Parameters.AddWithValue("$i",r.GetString(0));c.Parameters.AddWithValue("$f",r.GetString(2));c.Parameters.AddWithValue("$t",r.GetString(3));c.Parameters.AddWithValue("$a",r.GetDouble(4));c.Parameters.AddWithValue("$c",r.GetString(5));c.Parameters.AddWithValue("$d",r.IsDBNull(6)?"":r.GetString(6));c.Parameters.AddWithValue("$p",r.IsDBNull(7)?"":r.GetString(7));c.Parameters.AddWithValue("$z",r.GetInt32(8));c.Parameters.AddWithValue("$dt",DateTimeOffset.TryParse(r.GetString(1),out var dt)?dt.ToUnixTimeMilliseconds():DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());});}
    private static void CopyStockOperational(SqliteConnection db,SqliteTransaction tx){using var r=Query(db,tx,"SELECT Id,ProductId,ProductName,Type,QuantityChange,QuantityBefore,QuantityAfter,UnitCost,UnitPrice,ReferenceId,Date,Notes FROM StockMovements");while(r.Read())Exec(db,tx,"INSERT OR REPLACE INTO tbl_stock_movements(id,product_id,product_name,type,quantity_change,quantity_before,quantity_after,unit_cost,unit_price,reference_id,date,notes) VALUES($i,$p,$n,$t,$q,$b,$a,$c,$u,$r,$d,$x)",c=>{c.Parameters.AddWithValue("$i",r.GetString(0));c.Parameters.AddWithValue("$p",r.GetString(1));c.Parameters.AddWithValue("$n",r.GetString(2));c.Parameters.AddWithValue("$t",r.GetString(3));c.Parameters.AddWithValue("$q",r.GetDouble(4));c.Parameters.AddWithValue("$b",r.GetDouble(5));c.Parameters.AddWithValue("$a",r.GetDouble(6));c.Parameters.AddWithValue("$c",r.GetDouble(7));c.Parameters.AddWithValue("$u",r.GetDouble(8));c.Parameters.AddWithValue("$r",r.IsDBNull(9)?"":r.GetString(9));c.Parameters.AddWithValue("$d",DateTimeOffset.TryParse(r.GetString(10),out var dt)?dt.ToUnixTimeMilliseconds():DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());c.Parameters.AddWithValue("$x",r.IsDBNull(11)?"":r.GetString(11));});}
    private static void CopyAccountsOperational(SqliteConnection db,SqliteTransaction tx)
    {
        using var r=Query(db,tx,"SELECT Id,Code,Name,Type,Currency FROM Accounts");
        while(r.Read())
        {
            var id=r.GetString(0);
            Exec(db,tx,"INSERT OR REPLACE INTO tbl_accounts(id,code,name_ar,type,currency) VALUES($i,$c,$n,$t,$u)",c=>{
                c.Parameters.AddWithValue("$i",id);
                c.Parameters.AddWithValue("$c",r.IsDBNull(1)?"":r.GetString(1));
                c.Parameters.AddWithValue("$n",r.GetString(2));
                c.Parameters.AddWithValue("$t",r.GetString(3));
                c.Parameters.AddWithValue("$u",r.IsDBNull(4)?"SYP":r.GetString(4));
            });
        }
    }

    private static void CopyJournalsOperational(SqliteConnection db,SqliteTransaction tx)
    {
        var accountMap = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        using (var ar = Query(db,tx,"SELECT id,code,name_ar FROM tbl_accounts"))
        {
            while (ar.Read())
            {
                var id = ar.GetString(0);
                accountMap[id] = id;
                if (!ar.IsDBNull(1)) accountMap[ar.GetString(1)] = id;
                if (!ar.IsDBNull(2)) accountMap[ar.GetString(2)] = id;
            }
        }

        using var r=Query(db,tx,"SELECT Id,Date,Description,Reference FROM JournalEntries");
        while(r.Read())
        {
            var entryId=r.GetString(0);
            Exec(db,tx,"INSERT OR REPLACE INTO tbl_journal_entries(id,entry_number,date,description,reference_id,is_cancelled) VALUES($i,0,$d,$x,$r,0)",c=>{
                c.Parameters.AddWithValue("$i",entryId);
                c.Parameters.AddWithValue("$d",DateTimeOffset.TryParse(r.GetString(1),out var dt)?dt.ToUnixTimeMilliseconds():DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                c.Parameters.AddWithValue("$x",r.GetString(2));
                c.Parameters.AddWithValue("$r",r.IsDBNull(3)?"":r.GetString(3));
            });

            using var lr=Query(db,tx,"SELECT rowid,Account,Debit,Credit FROM JournalLines WHERE EntryId=$i",("$i",entryId));
            while(lr.Read())
            {
                var accountText=lr.IsDBNull(1)?"":lr.GetString(1);
                var accountId=accountMap.TryGetValue(accountText,out var mapped) ? mapped : accountText;
                var lineId=entryId+"-"+lr.GetInt64(0);
                Exec(db,tx,"INSERT OR REPLACE INTO tbl_journal_lines(id,entry_id,account_id,account_code,account_name,debit,credit,currency) VALUES($i,$e,$a,$c,$n,$d,$cr,'SYP')",c=>{
                    c.Parameters.AddWithValue("$i",lineId);
                    c.Parameters.AddWithValue("$e",entryId);
                    c.Parameters.AddWithValue("$a",accountId);
                    c.Parameters.AddWithValue("$c",accountText);
                    c.Parameters.AddWithValue("$n",accountText);
                    c.Parameters.AddWithValue("$d",lr.GetDouble(2));
                    c.Parameters.AddWithValue("$cr",lr.GetDouble(3));
                });
            }
        }
    }

    static JsonArray ReadProducts(SqliteConnection db) { var a=new JsonArray(); using var c=db.CreateCommand(); c.CommandText="SELECT id,name,barcode,category,unit,cost_price,sale_price,wholesale_price_syp,special_price_syp,quantity,min_stock_alert,expiry_date,default_currency FROM tbl_products"; using var r=c.ExecuteReader(); while(r.Read()) a.Add(new JsonObject{{"id",r.GetString(0)},{"name",r.GetString(1)},{"barcode",r.IsDBNull(2)?"":r.GetString(2)},{"category",r.IsDBNull(3)?"عام":r.GetString(3)},{"unit",r.IsDBNull(4)?"قطعة":r.GetString(4)},{"costSyp",r.GetDouble(5)},{"priceSyp",r.GetDouble(6)},{"priceWholesaleSyp",r.GetDouble(7)},{"priceSpecialSyp",r.GetDouble(8)},{"stock",r.GetDouble(9)},{"minStockAlert",r.GetDouble(10)},{"expiryDate",r.IsDBNull(11)?0:r.GetInt64(11)},{"createdAt",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}); return a; }
    static JsonArray ReadParties(SqliteConnection db) { var a=new JsonArray(); using var c=db.CreateCommand(); c.CommandText="SELECT id,name,phone,address,type,currency,credit_limit,price_tier,balance FROM tbl_parties"; using var r=c.ExecuteReader(); while(r.Read()) a.Add(new JsonObject{{"id",r.GetString(0)},{"name",r.GetString(1)},{"phone",r.IsDBNull(2)?"":r.GetString(2)},{"address",r.IsDBNull(3)?"":r.GetString(3)},{"type",r.GetString(4)=="SUPPLIER"?"SUPPLIER":"CUSTOMER"},{"defaultCurrency",r.IsDBNull(5)?"SYP":r.GetString(5)},{"creditLimit",r.GetDouble(6)},{"priceTier",r.IsDBNull(7)?"RETAIL":r.GetString(7)},{"createdAt",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}); return a; }
    static JsonArray ReadInvoices(SqliteConnection db) { var a=new JsonArray(); using var c=db.CreateCommand(); c.CommandText="SELECT id,number,type,party_id,party_name,net_amount,discount,fees_amount,fees_note,paid_amount,currency,date,is_cancelled,status,notes,payment_method,fund_id FROM tbl_invoices"; using var r=c.ExecuteReader(); while(r.Read()){var o=new JsonObject{{"id",r.GetString(0)},{"number",r.GetString(1)},{"type",r.GetString(2)},{"partyId",r.IsDBNull(3)?"":r.GetString(3)},{"partyName",r.IsDBNull(4)?"":r.GetString(4)},{"discount",r.GetDouble(6)},{"feesAmount",r.GetDouble(7)},{"feesNote",r.IsDBNull(8)?"":r.GetString(8)},{"paidAmount",r.GetDouble(9)},{"currency",r.GetString(10)},{"date",r.GetInt64(11)},{"status",(r.IsDBNull(13)?"CLOSED":r.GetString(13))},{"notes",r.IsDBNull(14)?"":r.GetString(14)},{"paymentMethod",r.IsDBNull(15)?"CASH":r.GetString(15)},{"fundId",r.IsDBNull(16)?"main_cash":r.GetString(16)}}; using var itemDb = new SqliteConnection(db.ConnectionString); itemDb.Open(); var iq=itemDb.CreateCommand(); iq.CommandText="SELECT id,product_id,product_name,quantity,unit_price,unit_cost,unit FROM tbl_invoice_items WHERE invoice_id=$i"; iq.Parameters.AddWithValue("$i",r.GetString(0)); using var ir=iq.ExecuteReader(); var items=new JsonArray(); while(ir.Read()) items.Add(new JsonObject{{"id",ir.GetString(0)},{"productId",ir.IsDBNull(1)?null:ir.GetString(1)},{"name",ir.GetString(2)},{"quantity",ir.GetDouble(3)},{"unitPrice",ir.GetDouble(4)},{"unitCost",ir.GetDouble(5)},{"unit",ir.IsDBNull(6)?"قطعة":ir.GetString(6)}}); o["items"]=items; a.Add(o);} return a; }
    static JsonArray ReadCash(SqliteConnection db) { var a=new JsonArray(); using var c=db.CreateCommand(); c.CommandText="SELECT id,type,amount,currency,description,category,party_id,party_name,invoice_id,tx_number,date,is_cancelled,payment_method,fund_id,target_fund_id,exchange_rate,target_amount,target_currency,cashier_name FROM tbl_cash_transactions"; using var r=c.ExecuteReader(); while(r.Read()) a.Add(new JsonObject{{"id",r.GetString(0)},{"type",r.GetString(1)},{"amount",r.GetDouble(2)},{"currency",r.GetString(3)},{"description",r.GetString(4)},{"category",r.IsDBNull(5)?"عام":r.GetString(5)},{"partyId",r.IsDBNull(6)?null:r.GetString(6)},{"partyName",r.IsDBNull(7)?null:r.GetString(7)},{"invoiceId",r.IsDBNull(8)?null:r.GetString(8)},{"voucherNumber",r.GetString(9)},{"date",r.GetInt64(10)},{"isCancelled",r.GetInt64(11)!=0},{"paymentMethod",r.IsDBNull(12)?"CASH":r.GetString(12)},{"fundId",r.IsDBNull(13)?"main_cash":r.GetString(13)},{"targetFundId",r.IsDBNull(14)?null:r.GetString(14)},{"exchangeRate",r.GetDouble(15)},{"targetAmount",r.GetDouble(16)},{"targetCurrency",r.IsDBNull(17)?null:r.GetString(17)},{"cashierName",r.IsDBNull(18)?"":r.GetString(18)}}); return a; }
    static JsonArray ReadStock(SqliteConnection db) { var a=new JsonArray(); using var c=db.CreateCommand(); c.CommandText="SELECT id,product_id,product_name,type,quantity_change,quantity_before,quantity_after,unit_cost,unit_price,currency,reference_type,reference_id,date,notes FROM tbl_stock_movements"; using var r=c.ExecuteReader(); while(r.Read()) a.Add(new JsonObject{{"id",r.GetString(0)},{"productId",r.GetString(1)},{"productName",r.GetString(2)},{"type",r.GetString(3)},{"quantityChange",r.GetDouble(4)},{"quantityBefore",r.GetDouble(5)},{"quantityAfter",r.GetDouble(6)},{"unitCost",r.IsDBNull(7)?0:r.GetDouble(7)},{"unitPrice",r.IsDBNull(8)?0:r.GetDouble(8)},{"currency",r.IsDBNull(9)?"SYP":r.GetString(9)},{"referenceType",r.IsDBNull(10)?"":r.GetString(10)},{"referenceId",r.IsDBNull(11)?"":r.GetString(11)},{"date",r.GetInt64(12)},{"notes",r.IsDBNull(13)?"":r.GetString(13)}}); return a; }
    static JsonArray ReadPayments(SqliteConnection db) { var a=new JsonArray(); using var c=db.CreateCommand(); c.CommandText="SELECT id,payment_number,invoice_id,party_id,party_name,fund_id,amount,currency,type,payment_method,date,is_cancelled FROM tbl_payments"; using var r=c.ExecuteReader(); while(r.Read()) a.Add(new JsonObject{{"id",r.GetString(0)},{"paymentNumber",r.IsDBNull(1)?0:r.GetInt64(1)},{"invoiceId",r.IsDBNull(2)?null:r.GetString(2)},{"partyId",r.IsDBNull(3)?null:r.GetString(3)},{"partyName",r.IsDBNull(4)?null:r.GetString(4)},{"fundId",r.IsDBNull(5)?"main_cash":r.GetString(5)},{"amount",r.GetDouble(6)},{"currency",r.GetString(7)},{"type",r.GetString(8)},{"paymentMethod",r.IsDBNull(9)?"CASH":r.GetString(9)},{"date",r.GetInt64(10)},{"isCancelled",r.GetInt64(11)!=0}}); return a; }
    static JsonArray ReadJournals(SqliteConnection db) { var a=new JsonArray(); using var c=db.CreateCommand(); c.CommandText="SELECT id,entry_number,date,description,reference_type,reference_id,is_cancelled FROM tbl_journal_entries"; using var r=c.ExecuteReader(); while(r.Read()){var o=new JsonObject{{"id",r.GetString(0)},{"entryNumber",r.GetInt64(1)},{"date",r.GetInt64(2)},{"description",r.GetString(3)},{"referenceType",r.IsDBNull(4)?"":r.GetString(4)},{"referenceId",r.IsDBNull(5)?"":r.GetString(5)},{"isCancelled",r.GetInt64(6)!=0}}; using var lineDb = new SqliteConnection(db.ConnectionString); lineDb.Open(); var l=lineDb.CreateCommand(); l.CommandText="SELECT id,account_id,account_code,account_name,debit,credit,currency FROM tbl_journal_lines WHERE entry_id=$i"; l.Parameters.AddWithValue("$i",r.GetString(0)); using var lr=l.ExecuteReader(); var lines=new JsonArray(); while(lr.Read()) lines.Add(new JsonObject{{"id",lr.GetString(0)},{"accountId",lr.GetString(1)},{"accountCode",lr.IsDBNull(2)?"":lr.GetString(2)},{"accountName",lr.IsDBNull(3)?"":lr.GetString(3)},{"debit",lr.GetDouble(4)},{"credit",lr.GetDouble(5)},{"currency",lr.IsDBNull(6)?"SYP":lr.GetString(6)}}); o["lines"]=lines; a.Add(o);} return a; }
    static JsonArray ReadAccounts(SqliteConnection db) { var a=new JsonArray(); using var c=db.CreateCommand(); c.CommandText="SELECT id,code,name_ar,type,currency FROM tbl_accounts"; using var r=c.ExecuteReader(); while(r.Read()) a.Add(new JsonObject{{"id",r.GetString(0)},{"code",r.GetString(1)},{"nameAr",r.GetString(2)},{"type",r.GetString(3)},{"currency",r.IsDBNull(4)?"SYP":r.GetString(4)}}); return a; }
    static JsonArray ReadAudits(SqliteConnection db) { var a=new JsonArray(); using var c=db.CreateCommand(); c.CommandText="SELECT id,action,entity_type,entity_id,description,performed_by,timestamp FROM tbl_audit_logs"; using var r=c.ExecuteReader(); while(r.Read()) a.Add(new JsonObject{{"id",r.GetString(0)},{"action",r.GetString(1)},{"entityType",r.GetString(2)},{"entityId",r.IsDBNull(3)?"":r.GetString(3)},{"description",r.IsDBNull(4)?"":r.GetString(4)},{"performedBy",r.IsDBNull(5)?"":r.GetString(5)},{"timestamp",r.GetInt64(6)}}); return a; }
    static JsonArray ReadUsers(SqliteConnection db) { var a=new JsonArray(); using var c=db.CreateCommand(); c.CommandText="SELECT id,name,role,pin_hash,pin_salt FROM tbl_users"; using var r=c.ExecuteReader(); while(r.Read()) a.Add(new JsonObject{{"id",r.GetString(0)},{"name",r.GetString(1)},{"role",r.GetString(2)},{"pinHash",r.IsDBNull(3)?"":r.GetString(3)},{"pinSalt",r.IsDBNull(4)?"":r.GetString(4)},{"isActive",true}}); return a; }

    static int UpsertProducts(SqliteConnection db, SqliteTransaction tx, JsonElement root){int n=0;if(!root.TryGetProperty("products",out var a))return 0;foreach(var x in a.EnumerateArray()){Exec(db,tx,"INSERT INTO tbl_products(id,name,barcode,category,unit,cost_price,sale_price,quantity,min_stock_alert,expiry_date,default_currency,raw_json) VALUES($i,$n,$b,$c,$u,$cp,$sp,$q,$m,$e,$cu,$raw) ON CONFLICT(id) DO UPDATE SET name=$n,barcode=$b,category=$c,unit=$u,cost_price=$cp,sale_price=$sp,quantity=$q,min_stock_alert=$m,expiry_date=$e,default_currency=$cu,raw_json=$raw",c=>{c.Parameters.AddWithValue("$i",S(x,"id"));c.Parameters.AddWithValue("$n",S(x,"name"));c.Parameters.AddWithValue("$b",S(x,"barcode"));c.Parameters.AddWithValue("$c",S(x,"category","عام"));c.Parameters.AddWithValue("$u",S(x,"unit","قطعة"));c.Parameters.AddWithValue("$cp",D(x,"costSyp",D(x,"cost_price")));c.Parameters.AddWithValue("$sp",D(x,"priceSyp",D(x,"sale_price")));c.Parameters.AddWithValue("$q",D(x,"stock",D(x,"quantity")));c.Parameters.AddWithValue("$m",D(x,"minStockAlert"));c.Parameters.AddWithValue("$e",L(x,"expiryDate"));c.Parameters.AddWithValue("$cu",S(x,"defaultCurrency",S(x,"currency","SYP")));c.Parameters.AddWithValue("$raw",x.GetRawText());});n++;}return n;}
    static int UpsertParties(SqliteConnection db,SqliteTransaction tx,JsonElement root){int n=0;if(!root.TryGetProperty("parties",out var a))return 0;foreach(var x in a.EnumerateArray()){Exec(db,tx,"INSERT INTO tbl_parties(id,name,phone,address,type,default_currency,credit_limit,price_tier,balance,currency,raw_json) VALUES($i,$n,$p,$a,$t,$cu,$cl,$pt,$b,$cu,$raw) ON CONFLICT(id) DO UPDATE SET name=$n,phone=$p,address=$a,type=$t,default_currency=$cu,credit_limit=$cl,price_tier=$pt,balance=$b,currency=$cu,raw_json=$raw",c=>{c.Parameters.AddWithValue("$i",S(x,"id"));c.Parameters.AddWithValue("$n",S(x,"name"));c.Parameters.AddWithValue("$p",S(x,"phone"));c.Parameters.AddWithValue("$a",S(x,"address"));c.Parameters.AddWithValue("$t",S(x,"type","CUSTOMER"));c.Parameters.AddWithValue("$cu",S(x,"defaultCurrency", "SYP"));c.Parameters.AddWithValue("$cl",D(x,"creditLimit"));c.Parameters.AddWithValue("$pt",S(x,"priceTier","RETAIL"));c.Parameters.AddWithValue("$b",D(x,"balance"));c.Parameters.AddWithValue("$raw",x.GetRawText());});n++;}return n;}
    static int UpsertInvoices(SqliteConnection db,SqliteTransaction tx,JsonElement root){int n=0;if(!root.TryGetProperty("invoices",out var a))return 0;foreach(var x in a.EnumerateArray()){var id=S(x,"id");Exec(db,tx,"INSERT INTO tbl_invoices(id,number,type,party_id,party_name,net_amount,paid_amount,remaining_amount,discount,fees_amount,fees_note,currency,date,is_cancelled,status,notes,payment_method,fund_id,raw_json) VALUES($i,$no,$t,$p,$pn,$net,$pa,$rem,$di,$f,$fn,$cu,$d,$can,$st,$nt,$pm,$fu,$raw) ON CONFLICT(id) DO UPDATE SET number=$no,type=$t,party_id=$p,party_name=$pn,net_amount=$net,paid_amount=$pa,remaining_amount=$rem,discount=$di,fees_amount=$f,fees_note=$fn,currency=$cu,date=$d,is_cancelled=$can,status=$st,notes=$nt,payment_method=$pm,fund_id=$fu,raw_json=$raw",c=>{var total=D(x,"total",D(x,"subtotal")-D(x,"discount")+D(x,"feesAmount"));var paid=D(x,"paidAmount");c.Parameters.AddWithValue("$i",id);c.Parameters.AddWithValue("$no",S(x,"number"));c.Parameters.AddWithValue("$t",S(x,"type","SALE"));c.Parameters.AddWithValue("$p",string.IsNullOrWhiteSpace(S(x,"partyId"))?DBNull.Value:S(x,"partyId"));c.Parameters.AddWithValue("$pn",S(x,"partyName"));c.Parameters.AddWithValue("$net",total);c.Parameters.AddWithValue("$pa",paid);c.Parameters.AddWithValue("$rem",Math.Max(0,total-paid));c.Parameters.AddWithValue("$di",D(x,"discount"));c.Parameters.AddWithValue("$f",D(x,"feesAmount"));c.Parameters.AddWithValue("$fn",S(x,"feesNote"));c.Parameters.AddWithValue("$cu",S(x,"currency","SYP"));c.Parameters.AddWithValue("$d",L(x,"date"));c.Parameters.AddWithValue("$can",S(x,"status")=="CANCELLED"?1:0);c.Parameters.AddWithValue("$st",S(x,"status","CLOSED"));c.Parameters.AddWithValue("$nt",S(x,"notes"));c.Parameters.AddWithValue("$pm",S(x,"paymentMethod","CASH"));c.Parameters.AddWithValue("$fu",S(x,"fundId","main_cash"));c.Parameters.AddWithValue("$raw",x.GetRawText());});Exec(db,tx,"DELETE FROM tbl_invoice_items WHERE invoice_id=$i",c=>c.Parameters.AddWithValue("$i",id));if(x.TryGetProperty("items",out var items))foreach(var it in items.EnumerateArray())Exec(db,tx,"INSERT INTO tbl_invoice_items(id,invoice_id,product_id,product_name,quantity,unit_price,unit_cost,unit, total_price,raw_json) VALUES($i,$v,$p,$n,$q,$u,$c,$un,$t,$raw)",c=>{c.Parameters.AddWithValue("$i",S(it,"id",Guid.NewGuid().ToString()));c.Parameters.AddWithValue("$v",id);c.Parameters.AddWithValue("$p",string.IsNullOrWhiteSpace(S(it,"productId"))?DBNull.Value:S(it,"productId"));c.Parameters.AddWithValue("$n",S(it,"name"));c.Parameters.AddWithValue("$q",D(it,"quantity",1));c.Parameters.AddWithValue("$u",D(it,"unitPrice"));c.Parameters.AddWithValue("$c",D(it,"unitCost"));c.Parameters.AddWithValue("$un",S(it,"unit","قطعة"));c.Parameters.AddWithValue("$t",D(it,"quantity",1)*D(it,"unitPrice"));c.Parameters.AddWithValue("$raw",it.GetRawText());});n++;}return n;}
    static int UpsertPayments(SqliteConnection db,SqliteTransaction tx,JsonElement root){int n=0;if(!root.TryGetProperty("payments",out var a))return 0;foreach(var x in a.EnumerateArray()){Exec(db,tx,"INSERT INTO tbl_payments(id,payment_number,invoice_id,party_id,party_name,fund_id,amount,currency,type,payment_method,date,is_cancelled,raw_json) VALUES($i,$n,$v,$p,$pn,$f,$a,$c,$t,$m,$d,$z,$raw) ON CONFLICT(id) DO UPDATE SET payment_number=$n,invoice_id=$v,party_id=$p,party_name=$pn,fund_id=$f,amount=$a,currency=$c,type=$t,payment_method=$m,date=$d,is_cancelled=$z,raw_json=$raw",c=>{c.Parameters.AddWithValue("$i",S(x,"id"));c.Parameters.AddWithValue("$n",L(x,"paymentNumber"));c.Parameters.AddWithValue("$v",S(x,"invoiceId"));c.Parameters.AddWithValue("$p",string.IsNullOrWhiteSpace(S(x,"partyId"))?DBNull.Value:S(x,"partyId"));c.Parameters.AddWithValue("$pn",S(x,"partyName"));c.Parameters.AddWithValue("$f",S(x,"fundId","main_cash"));c.Parameters.AddWithValue("$a",D(x,"amount"));c.Parameters.AddWithValue("$c",S(x,"currency","SYP"));c.Parameters.AddWithValue("$t",S(x,"type","INVOICE_PAYMENT"));c.Parameters.AddWithValue("$m",S(x,"paymentMethod","CASH"));c.Parameters.AddWithValue("$d",L(x,"date"));c.Parameters.AddWithValue("$z",B(x,"isCancelled")?1:0);c.Parameters.AddWithValue("$raw",x.GetRawText());});n++;}return n;}
    static int UpsertCash(SqliteConnection db,SqliteTransaction tx,JsonElement root){int n=0;if(!root.TryGetProperty("cashTransactions",out var a))return 0;foreach(var x in a.EnumerateArray()){Exec(db,tx,"INSERT INTO tbl_cash_transactions(id,tx_number,fund_id,type,amount,currency,description,category,party_id,party_name,invoice_id,cashier_name,payment_method,is_cancelled,target_fund_id,exchange_rate,target_amount,target_currency,date,raw_json) VALUES($i,$n,$f,$t,$a,$c,$d,$g,$p,$pn,$v,$cash,$m,$z,$tf,$er,$ta,$tc,$dt,$raw) ON CONFLICT(id) DO UPDATE SET tx_number=$n,fund_id=$f,type=$t,amount=$a,currency=$c,description=$d,category=$g,party_id=$p,party_name=$pn,invoice_id=$v,cashier_name=$cash,payment_method=$m,is_cancelled=$z,target_fund_id=$tf,exchange_rate=$er,target_amount=$ta,target_currency=$tc,date=$dt,raw_json=$raw",c=>{c.Parameters.AddWithValue("$i",S(x,"id"));c.Parameters.AddWithValue("$n",S(x,"voucherNumber"));c.Parameters.AddWithValue("$f",S(x,"fundId","main_cash"));c.Parameters.AddWithValue("$t",S(x,"type","IN"));c.Parameters.AddWithValue("$a",D(x,"amount"));c.Parameters.AddWithValue("$c",S(x,"currency","SYP"));c.Parameters.AddWithValue("$d",S(x,"description"));c.Parameters.AddWithValue("$g",S(x,"category","عام"));c.Parameters.AddWithValue("$p",string.IsNullOrWhiteSpace(S(x,"partyId"))?DBNull.Value:S(x,"partyId"));c.Parameters.AddWithValue("$pn",S(x,"partyName"));c.Parameters.AddWithValue("$v",S(x,"invoiceId"));c.Parameters.AddWithValue("$cash",S(x,"cashierName"));c.Parameters.AddWithValue("$m",S(x,"paymentMethod","CASH"));c.Parameters.AddWithValue("$z",B(x,"isCancelled")?1:0);c.Parameters.AddWithValue("$tf",S(x,"targetFundId"));c.Parameters.AddWithValue("$er",D(x,"exchangeRate",1));c.Parameters.AddWithValue("$ta",D(x,"targetAmount"));c.Parameters.AddWithValue("$tc",S(x,"targetCurrency"));c.Parameters.AddWithValue("$dt",L(x,"date"));c.Parameters.AddWithValue("$raw",x.GetRawText());});n++;}return n;}
    static int UpsertStock(SqliteConnection db,SqliteTransaction tx,JsonElement root){int n=0;if(!root.TryGetProperty("stockMovements",out var a))return 0;foreach(var x in a.EnumerateArray()){Exec(db,tx,"INSERT INTO tbl_stock_movements(id,product_id,product_name,type,quantity_change,quantity_before,quantity_after,unit_cost,unit_price,currency,reference_type,reference_id,date,notes,raw_json) VALUES($i,$p,$pn,$t,$q,$b,$af,$c,$u,$cu,$rt,$ri,$d,$n,$raw) ON CONFLICT(id) DO UPDATE SET quantity_change=$q,quantity_before=$b,quantity_after=$af,unit_cost=$c,unit_price=$u,date=$d,notes=$n,raw_json=$raw",c=>{c.Parameters.AddWithValue("$i",S(x,"id"));c.Parameters.AddWithValue("$p",S(x,"productId"));c.Parameters.AddWithValue("$pn",S(x,"productName"));c.Parameters.AddWithValue("$t",S(x,"type","SALE"));c.Parameters.AddWithValue("$q",D(x,"quantityChange"));c.Parameters.AddWithValue("$b",D(x,"quantityBefore"));c.Parameters.AddWithValue("$af",D(x,"quantityAfter"));c.Parameters.AddWithValue("$c",D(x,"unitCost"));c.Parameters.AddWithValue("$u",D(x,"unitPrice"));c.Parameters.AddWithValue("$cu",S(x,"currency","SYP"));c.Parameters.AddWithValue("$rt",S(x,"referenceType"));c.Parameters.AddWithValue("$ri",S(x,"referenceId"));c.Parameters.AddWithValue("$d",L(x,"date"));c.Parameters.AddWithValue("$n",S(x,"notes"));c.Parameters.AddWithValue("$raw",x.GetRawText());});n++;}return n;}
    static int UpsertJournals(SqliteConnection db,SqliteTransaction tx,JsonElement root){int n=0;if(!root.TryGetProperty("journalEntries",out var a))return 0;foreach(var x in a.EnumerateArray()){var id=S(x,"id");Exec(db,tx,"INSERT INTO tbl_journal_entries(id,entry_number,date,description,reference_type,reference_id,is_cancelled,raw_json) VALUES($i,$n,$d,$x,$t,$r,$c,$raw) ON CONFLICT(id) DO UPDATE SET entry_number=$n,date=$d,description=$x,reference_type=$t,reference_id=$r,is_cancelled=$c,raw_json=$raw",c=>{c.Parameters.AddWithValue("$i",id);c.Parameters.AddWithValue("$n",L(x,"entryNumber"));c.Parameters.AddWithValue("$d",L(x,"date"));c.Parameters.AddWithValue("$x",S(x,"description"));c.Parameters.AddWithValue("$t",S(x,"referenceType"));c.Parameters.AddWithValue("$r",S(x,"referenceId"));c.Parameters.AddWithValue("$c",B(x,"isCancelled")?1:0);c.Parameters.AddWithValue("$raw",x.GetRawText());});Exec(db,tx,"DELETE FROM tbl_journal_lines WHERE entry_id=$i",c=>c.Parameters.AddWithValue("$i",id));if(x.TryGetProperty("lines",out var lines))foreach(var l in lines.EnumerateArray())Exec(db,tx,"INSERT INTO tbl_journal_lines(id,entry_id,account_id,account_code,account_name,debit,credit,currency,raw_json) VALUES($i,$e,$a,$c,$n,$d,$cr,$cu,$raw)",c=>{c.Parameters.AddWithValue("$i",S(l,"id",Guid.NewGuid().ToString()));c.Parameters.AddWithValue("$e",id);c.Parameters.AddWithValue("$a",S(l,"accountId"));c.Parameters.AddWithValue("$c",S(l,"accountCode"));c.Parameters.AddWithValue("$n",S(l,"accountName"));c.Parameters.AddWithValue("$d",D(l,"debit"));c.Parameters.AddWithValue("$cr",D(l,"credit"));c.Parameters.AddWithValue("$cu",S(l,"currency","SYP"));c.Parameters.AddWithValue("$raw",l.GetRawText());});n++;}return n;}
    static void UpsertAccounts(SqliteConnection db,SqliteTransaction tx,JsonElement root){if(!root.TryGetProperty("accounts",out var a))return;foreach(var x in a.EnumerateArray())Exec(db,tx,"INSERT INTO tbl_accounts(id,code,name_ar,type,currency,raw_json) VALUES($i,$c,$n,$t,$u,$r) ON CONFLICT(id) DO UPDATE SET code=$c,name_ar=$n,type=$t,currency=$u,raw_json=$r",c=>{c.Parameters.AddWithValue("$i",S(x,"id"));c.Parameters.AddWithValue("$c",S(x,"code"));c.Parameters.AddWithValue("$n",S(x,"nameAr"));c.Parameters.AddWithValue("$t",S(x,"type","ASSET"));c.Parameters.AddWithValue("$u",S(x,"currency","SYP"));c.Parameters.AddWithValue("$r",x.GetRawText());});}
    static void UpsertAudits(SqliteConnection db,SqliteTransaction tx,JsonElement root){if(!root.TryGetProperty("auditLogs",out var a))return;foreach(var x in a.EnumerateArray())Exec(db,tx,"INSERT OR REPLACE INTO tbl_audit_logs(id,action,entity_type,entity_id,description,performed_by,timestamp,raw_json) VALUES($i,$a,$t,$e,$d,$p,$ts,$r)",c=>{c.Parameters.AddWithValue("$i",S(x,"id"));c.Parameters.AddWithValue("$a",S(x,"action"));c.Parameters.AddWithValue("$t",S(x,"entityType"));c.Parameters.AddWithValue("$e",S(x,"entityId"));c.Parameters.AddWithValue("$d",S(x,"description"));c.Parameters.AddWithValue("$p",S(x,"performedBy"));c.Parameters.AddWithValue("$ts",L(x,"timestamp"));c.Parameters.AddWithValue("$r",x.GetRawText());});}
    static void UpsertUsers(SqliteConnection db,SqliteTransaction tx,JsonElement root){if(!root.TryGetProperty("users",out var a))return;foreach(var x in a.EnumerateArray())Exec(db,tx,"INSERT INTO tbl_users(id,name,role,pin_hash,pin_salt,raw_json) VALUES($i,$n,$r,$h,$s,$j) ON CONFLICT(id) DO UPDATE SET name=$n,role=$r,pin_hash=$h,pin_salt=$s,raw_json=$j",c=>{c.Parameters.AddWithValue("$i",S(x,"id"));c.Parameters.AddWithValue("$n",S(x,"name"));c.Parameters.AddWithValue("$r",S(x,"role","CASHIER"));c.Parameters.AddWithValue("$h",S(x,"pinHash"));c.Parameters.AddWithValue("$s",S(x,"pinSalt"));c.Parameters.AddWithValue("$j",x.GetRawText());});}
    static void UpsertLicense(SqliteConnection db,SqliteTransaction tx,JsonElement root){if(!root.TryGetProperty("license",out var x)||x.ValueKind==JsonValueKind.Null)return;Exec(db,tx,"INSERT OR REPLACE INTO tbl_license(id,license_id,device_id,plan,status,digital_signature,raw_json) VALUES('android-license',$i,$d,$p,$s,$g,$r)",c=>{c.Parameters.AddWithValue("$i",S(x,"licenseId"));c.Parameters.AddWithValue("$d",S(x,"deviceId"));c.Parameters.AddWithValue("$p",S(x,"plan"));c.Parameters.AddWithValue("$s",S(x,"status"));c.Parameters.AddWithValue("$g",S(x,"digitalSignature"));c.Parameters.AddWithValue("$r",x.GetRawText());});}



    private static void MirrorParityToOperational(SqliteConnection db)
    {
        using var tx = db.BeginTransaction();
        try
        {
            Exec(db, tx, "DELETE FROM InvoiceLines", _ => { });
            Exec(db, tx, "DELETE FROM Payments", _ => { });
            Exec(db, tx, "DELETE FROM CashTransactions", _ => { });
            Exec(db, tx, "DELETE FROM StockMovements", _ => { });
            Exec(db, tx, "DELETE FROM JournalLines", _ => { });
            Exec(db, tx, "DELETE FROM JournalEntries", _ => { });
            Exec(db, tx, "DELETE FROM AuditLogs", _ => { });

            using (var r = Query(db, tx, "SELECT id,barcode,name,unit,cost_price,sale_price,quantity,min_stock_alert FROM tbl_products"))
            while (r.Read())
                Exec(db, tx,
                    @"INSERT INTO Products(Id,Code,Barcode,Name,Unit,PurchasePrice,SalePrice,Quantity,MinQuantity,IsActive)
                      VALUES($i,'',$b,$n,$u,$pp,$sp,$q,$m,1)
                      ON CONFLICT(Id) DO UPDATE SET Barcode=$b,Name=$n,Unit=$u,PurchasePrice=$pp,SalePrice=$sp,Quantity=$q,MinQuantity=$m,IsActive=1",
                    c => {
                        c.Parameters.AddWithValue("$i", r.GetString(0));
                        c.Parameters.AddWithValue("$b", r.IsDBNull(1) ? "" : r.GetString(1));
                        c.Parameters.AddWithValue("$n", r.GetString(2));
                        c.Parameters.AddWithValue("$u", r.IsDBNull(3) ? "قطعة" : r.GetString(3));
                        c.Parameters.AddWithValue("$pp", r.GetDouble(4));
                        c.Parameters.AddWithValue("$sp", r.GetDouble(5));
                        c.Parameters.AddWithValue("$q", r.GetDouble(6));
                        c.Parameters.AddWithValue("$m", r.GetDouble(7));
                    });

            using (var r = Query(db, tx, "SELECT id,name,phone,address,type,balance FROM tbl_parties"))
            while (r.Read())
                Exec(db, tx,
                    @"INSERT INTO Parties(Id,Name,Phone,Address,Type,Balance)
                      VALUES($i,$n,$p,$a,$t,$b)
                      ON CONFLICT(Id) DO UPDATE SET Name=$n,Phone=$p,Address=$a,Type=$t,Balance=$b",
                    c => {
                        c.Parameters.AddWithValue("$i", r.GetString(0));
                        c.Parameters.AddWithValue("$n", r.GetString(1));
                        c.Parameters.AddWithValue("$p", r.IsDBNull(2) ? "" : r.GetString(2));
                        c.Parameters.AddWithValue("$a", r.IsDBNull(3) ? "" : r.GetString(3));
                        c.Parameters.AddWithValue("$t", string.Equals(r.GetString(4), "SUPPLIER", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                        c.Parameters.AddWithValue("$b", r.IsDBNull(5) ? 0d : r.GetDouble(5));
                    });

            using (var r = Query(db, tx, "SELECT id,number,type,party_id,party_name,net_amount,discount,fees_amount,fees_note,paid_amount,remaining_amount,currency,date,status,notes,payment_method FROM tbl_invoices"))
            while (r.Read())
            {
                var id = r.GetString(0);
                var type = r.GetString(2).ToUpperInvariant() switch {
                    "PURCHASE" => 1,
                    "RETURN_SALE" or "SALE_RETURN" => 2,
                    "RETURN_PURCHASE" or "PURCHASE_RETURN" => 3,
                    "QUOTATION" => 4,
                    _ => 0
                };
                var date = DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(12)).LocalDateTime.ToString("O");
                Exec(db, tx,
                    @"INSERT INTO Invoices(Id,Number,Date,Type,PartyId,PartyName,Total,IsCredit,Discount,FeesAmount,FeesNote,PaidAmount,PaymentMethod,Currency,Notes,Status)
                      VALUES($i,$n,$d,$t,$p,$pn,$tot,$cr,$di,$f,$fn,$pa,$pm,$cu,$nt,$st)
                      ON CONFLICT(Id) DO UPDATE SET Number=$n,Date=$d,Type=$t,PartyId=$p,PartyName=$pn,Total=$tot,IsCredit=$cr,Discount=$di,FeesAmount=$f,FeesNote=$fn,PaidAmount=$pa,PaymentMethod=$pm,Currency=$cu,Notes=$nt,Status=$st",
                    c => {
                        c.Parameters.AddWithValue("$i", id);
                        c.Parameters.AddWithValue("$n", r.GetString(1));
                        c.Parameters.AddWithValue("$d", date);
                        c.Parameters.AddWithValue("$t", type);
                        c.Parameters.AddWithValue("$p", r.IsDBNull(3) ? "" : r.GetString(3));
                        c.Parameters.AddWithValue("$pn", r.IsDBNull(4) ? "" : r.GetString(4));
                        c.Parameters.AddWithValue("$tot", r.IsDBNull(5) ? 0d : r.GetDouble(5));
                        c.Parameters.AddWithValue("$cr", r.IsDBNull(10) ? 0 : (r.GetDouble(10) > 0.0001 ? 1 : 0));
                        c.Parameters.AddWithValue("$di", r.IsDBNull(6) ? 0d : r.GetDouble(6));
                        c.Parameters.AddWithValue("$f", r.IsDBNull(7) ? 0d : r.GetDouble(7));
                        c.Parameters.AddWithValue("$fn", r.IsDBNull(8) ? "" : r.GetString(8));
                        c.Parameters.AddWithValue("$pa", r.IsDBNull(9) ? 0d : r.GetDouble(9));
                        c.Parameters.AddWithValue("$pm", r.IsDBNull(15) ? "CASH" : r.GetString(15));
                        c.Parameters.AddWithValue("$cu", r.IsDBNull(11) ? "SYP" : r.GetString(11));
                        c.Parameters.AddWithValue("$nt", r.IsDBNull(14) ? "" : r.GetString(14));
                        c.Parameters.AddWithValue("$st", r.IsDBNull(13) ? "CLOSED" : r.GetString(13));
                    });

                using var ir = Query(db, tx, "SELECT id,product_id,product_name,quantity,unit_price,unit_cost,unit FROM tbl_invoice_items WHERE invoice_id=$i", ("$i", id));
                while (ir.Read())
                    Exec(db, tx,
                        "INSERT INTO InvoiceLines(InvoiceId,ProductId,ProductName,Quantity,UnitPrice,Total,UnitCost) VALUES($v,$p,$n,$q,$u,$t,$c)",
                        c => {
                            c.Parameters.AddWithValue("$v", id);
                            c.Parameters.AddWithValue("$p", ir.IsDBNull(1) ? "" : ir.GetString(1));
                            c.Parameters.AddWithValue("$n", ir.GetString(2));
                            c.Parameters.AddWithValue("$q", ir.GetDouble(3));
                            c.Parameters.AddWithValue("$u", ir.GetDouble(4));
                            c.Parameters.AddWithValue("$t", ir.GetDouble(3) * ir.GetDouble(4));
                            c.Parameters.AddWithValue("$c", ir.IsDBNull(5) ? 0d : ir.GetDouble(5));
                        });
            }

            using (var r = Query(db, tx, "SELECT id,date,type,party_id,party_name,amount,payment_method,currency,fund_id,invoice_id,is_cancelled FROM tbl_payments"))
            while (r.Read())
            {
                var type = r.GetString(2).ToUpperInvariant() switch {
                    "PARTY_PAYMENT" => 1,
                    "EXPENSE" => 3,
                    "CASH_INCOME" => 2,
                    _ => 0
                };
                Exec(db, tx,
                    @"INSERT INTO Payments(Id,Date,Type,PartyId,PartyName,Amount,Description,InvoiceId,Currency,FundId,Cancelled)
                      VALUES($i,$d,$t,$p,$pn,$a,'',$v,$c,$f,$z)
                      ON CONFLICT(Id) DO UPDATE SET Date=$d,Type=$t,PartyId=$p,PartyName=$pn,Amount=$a,InvoiceId=$v,Currency=$c,FundId=$f,Cancelled=$z",
                    c => {
                        c.Parameters.AddWithValue("$i", r.GetString(0));
                        c.Parameters.AddWithValue("$d", DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(1)).LocalDateTime.ToString("O"));
                        c.Parameters.AddWithValue("$t", type);
                        c.Parameters.AddWithValue("$p", r.IsDBNull(3) ? "" : r.GetString(3));
                        c.Parameters.AddWithValue("$pn", r.IsDBNull(4) ? "" : r.GetString(4));
                        c.Parameters.AddWithValue("$a", r.GetDouble(5));
                        c.Parameters.AddWithValue("$v", r.IsDBNull(9) ? "" : r.GetString(9));
                        c.Parameters.AddWithValue("$c", r.IsDBNull(7) ? "SYP" : r.GetString(7));
                        c.Parameters.AddWithValue("$f", r.IsDBNull(8) ? "MAIN-SYP" : r.GetString(8));
                        c.Parameters.AddWithValue("$z", r.GetInt64(10));
                    });
            }

            using (var r = Query(db, tx, "SELECT id,date,fund_id,type,amount,currency,description,party_id,is_cancelled FROM tbl_cash_transactions"))
            while (r.Read())
                Exec(db, tx,
                    @"INSERT INTO CashTransactions(Id,Date,FundId,Type,Amount,Currency,Description,ReferenceId,PartyId,IsCancelled)
                      VALUES($i,$d,$f,$t,$a,$c,$x,'',$p,$z)
                      ON CONFLICT(Id) DO UPDATE SET Date=$d,FundId=$f,Type=$t,Amount=$a,Currency=$c,Description=$x,PartyId=$p,IsCancelled=$z",
                    c => {
                        c.Parameters.AddWithValue("$i", r.GetString(0));
                        c.Parameters.AddWithValue("$d", DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(1)).LocalDateTime.ToString("O"));
                        c.Parameters.AddWithValue("$f", r.GetString(2));
                        c.Parameters.AddWithValue("$t", r.GetString(3));
                        c.Parameters.AddWithValue("$a", r.GetDouble(4));
                        c.Parameters.AddWithValue("$c", r.GetString(5));
                        c.Parameters.AddWithValue("$x", r.IsDBNull(6) ? "" : r.GetString(6));
                        c.Parameters.AddWithValue("$p", r.IsDBNull(7) ? "" : r.GetString(7));
                        c.Parameters.AddWithValue("$z", r.GetInt64(8));
                    });

            using (var r = Query(db, tx, "SELECT id,product_id,product_name,type,quantity_change,quantity_before,quantity_after,unit_cost,unit_price,reference_id,date,notes FROM tbl_stock_movements"))
            while (r.Read())
                Exec(db, tx,
                    @"INSERT INTO StockMovements(Id,ProductId,ProductName,Type,QuantityChange,QuantityBefore,QuantityAfter,UnitCost,UnitPrice,ReferenceId,Date,Notes)
                      VALUES($i,$p,$n,$t,$q,$b,$a,$c,$u,$r,$d,$x)
                      ON CONFLICT(Id) DO UPDATE SET ProductId=$p,ProductName=$n,Type=$t,QuantityChange=$q,QuantityBefore=$b,QuantityAfter=$a,UnitCost=$c,UnitPrice=$u,ReferenceId=$r,Date=$d,Notes=$x",
                    c => {
                        c.Parameters.AddWithValue("$i", r.GetString(0));
                        c.Parameters.AddWithValue("$p", r.GetString(1));
                        c.Parameters.AddWithValue("$n", r.GetString(2));
                        c.Parameters.AddWithValue("$t", r.GetString(3));
                        c.Parameters.AddWithValue("$q", r.GetDouble(4));
                        c.Parameters.AddWithValue("$b", r.GetDouble(5));
                        c.Parameters.AddWithValue("$a", r.GetDouble(6));
                        c.Parameters.AddWithValue("$c", r.IsDBNull(7) ? 0d : r.GetDouble(7));
                        c.Parameters.AddWithValue("$u", r.IsDBNull(8) ? 0d : r.GetDouble(8));
                        c.Parameters.AddWithValue("$r", r.IsDBNull(9) ? "" : r.GetString(9));
                        c.Parameters.AddWithValue("$d", DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(10)).LocalDateTime.ToString("O"));
                        c.Parameters.AddWithValue("$x", r.IsDBNull(11) ? "" : r.GetString(11));
                    });

            using (var r = Query(db, tx, "SELECT id,code,name_ar,type,currency FROM tbl_accounts"))
            while (r.Read())
                Exec(db, tx,
                    @"INSERT INTO Accounts(Id,Code,Name,Type,Currency) VALUES($i,$c,$n,$t,$u)
                      ON CONFLICT(Id) DO UPDATE SET Code=$c,Name=$n,Type=$t,Currency=$u",
                    c => {
                        c.Parameters.AddWithValue("$i", r.GetString(0));
                        c.Parameters.AddWithValue("$c", r.GetString(1));
                        c.Parameters.AddWithValue("$n", r.GetString(2));
                        c.Parameters.AddWithValue("$t", r.GetString(3));
                        c.Parameters.AddWithValue("$u", r.IsDBNull(4) ? "SYP" : r.GetString(4));
                    });

            using (var r = Query(db, tx, "SELECT id,entry_number,date,description,reference_id,is_cancelled FROM tbl_journal_entries"))
            while (r.Read())
            {
                var entryId = r.GetString(0);
                Exec(db, tx,
                    @"INSERT INTO JournalEntries(Id,Date,Description,Reference) VALUES($i,$d,$x,$r)
                      ON CONFLICT(Id) DO UPDATE SET Date=$d,Description=$x,Reference=$r",
                    c => {
                        c.Parameters.AddWithValue("$i", entryId);
                        c.Parameters.AddWithValue("$d", DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(2)).LocalDateTime.ToString("O"));
                        c.Parameters.AddWithValue("$x", r.GetString(3));
                        c.Parameters.AddWithValue("$r", r.IsDBNull(4) ? "" : r.GetString(4));
                    });

                using var lr = Query(db, tx, "SELECT account_name,debit,credit FROM tbl_journal_lines WHERE entry_id=$e", ("$e", entryId));
                while (lr.Read())
                    Exec(db, tx, "INSERT INTO JournalLines(EntryId,Account,Debit,Credit) VALUES($e,$a,$d,$c)",
                        c => {
                            c.Parameters.AddWithValue("$e", entryId);
                            c.Parameters.AddWithValue("$a", lr.IsDBNull(0) ? "" : lr.GetString(0));
                            c.Parameters.AddWithValue("$d", lr.IsDBNull(1) ? 0d : lr.GetDouble(1));
                            c.Parameters.AddWithValue("$c", lr.IsDBNull(2) ? 0d : lr.GetDouble(2));
                        });
            }

            using (var r = Query(db, tx, "SELECT id,name,role,pin_hash FROM tbl_users"))
            while (r.Read())
                Exec(db, tx,
                    @"INSERT INTO Users(Id,Name,Role,PinHash,IsActive,CreatedAt) VALUES($i,$n,$r,$p,1,$d)
                      ON CONFLICT(Id) DO UPDATE SET Name=$n,Role=$r,PinHash=$p,IsActive=1",
                    c => {
                        c.Parameters.AddWithValue("$i", r.GetString(0));
                        c.Parameters.AddWithValue("$n", r.GetString(1));
                        c.Parameters.AddWithValue("$r", r.GetString(2));
                        c.Parameters.AddWithValue("$p", r.IsDBNull(3) ? "" : r.GetString(3));
                        c.Parameters.AddWithValue("$d", DateTime.Now.ToString("O"));
                    });

            using (var r = Query(db, tx, "SELECT id,action,entity_type,entity_id,description,performed_by,timestamp FROM tbl_audit_logs"))
            while (r.Read())
                Exec(db, tx,
                    @"INSERT INTO AuditLogs(Id,Action,EntityType,EntityId,Description,PerformedBy,Timestamp)
                      VALUES($i,$a,$t,$e,$d,$p,$ts)
                      ON CONFLICT(Id) DO UPDATE SET Action=$a,EntityType=$t,EntityId=$e,Description=$d,PerformedBy=$p,Timestamp=$ts",
                    c => {
                        c.Parameters.AddWithValue("$i", r.GetString(0));
                        c.Parameters.AddWithValue("$a", r.GetString(1));
                        c.Parameters.AddWithValue("$t", r.GetString(2));
                        c.Parameters.AddWithValue("$e", r.IsDBNull(3) ? "" : r.GetString(3));
                        c.Parameters.AddWithValue("$d", r.IsDBNull(4) ? "" : r.GetString(4));
                        c.Parameters.AddWithValue("$p", r.IsDBNull(5) ? "" : r.GetString(5));
                        c.Parameters.AddWithValue("$ts", DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(6)).LocalDateTime.ToString("O"));
                    });

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static SqliteDataReader Query(SqliteConnection db, SqliteTransaction tx, string sql, params (string Name, object Value)[] parameters)
    {
        // Use a separate read connection so writes on the transaction connection
        // never run while a DataReader is open on that same connection.
        var readDb = new SqliteConnection(db.ConnectionString);
        readDb.Open();
        var cmd = readDb.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
    }

    static void Exec(SqliteConnection c,SqliteTransaction tx,string sql,Action<SqliteCommand> fill){using var q=c.CreateCommand();q.Transaction=tx;q.CommandText=sql;fill(q);q.ExecuteNonQuery();}
    static string S(JsonElement e,string n,string d=""){return e.TryGetProperty(n,out var x)&&x.ValueKind!=JsonValueKind.Null?x.ToString()??d:d;}
    static double D(JsonElement e,string n,double d=0){return e.TryGetProperty(n,out var x)&&x.TryGetDouble(out var v)?v:d;}
    static long L(JsonElement e,string n,long d=0){return e.TryGetProperty(n,out var x)&&x.TryGetInt64(out var v)?v:d;}
    static bool B(JsonElement e,string n){return e.TryGetProperty(n,out var x)&&x.ValueKind==JsonValueKind.True;}

}
