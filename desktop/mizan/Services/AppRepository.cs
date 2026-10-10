using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using MizanDesktop.Models;

namespace MizanDesktop.Services;

public sealed class AppRepository
{
    private readonly string _cs;
    public List<Product> Products { get; private set; } = [];
    public List<Party> Parties { get; private set; } = [];
    public List<Invoice> Invoices { get; private set; } = [];
    public List<Payment> Payments { get; private set; } = [];
    public List<JournalEntry> JournalEntries { get; private set; } = [];

    public AppRepository()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MizanDesktop");
        Directory.CreateDirectory(dir);
        _cs = $"Data Source={Path.Combine(dir, "mizan.db")}";
        Init();
        LoadAll();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        return c;
    }

    private void Init()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
PRAGMA foreign_keys=ON;
CREATE TABLE IF NOT EXISTS Products(Id TEXT PRIMARY KEY,Code TEXT,Barcode TEXT,Name TEXT NOT NULL,Unit TEXT,PurchasePrice REAL,SalePrice REAL,Quantity REAL,MinQuantity REAL,IsActive INTEGER);
CREATE TABLE IF NOT EXISTS Parties(Id TEXT PRIMARY KEY,Name TEXT NOT NULL,Phone TEXT,Address TEXT,Type INTEGER,Balance REAL);
CREATE TABLE IF NOT EXISTS Invoices(Id TEXT PRIMARY KEY,Number TEXT,Date TEXT,Type INTEGER,PartyId TEXT,PartyName TEXT,Total REAL,IsCredit INTEGER);
CREATE TABLE IF NOT EXISTS InvoiceLines(Id INTEGER PRIMARY KEY AUTOINCREMENT,InvoiceId TEXT,ProductId TEXT,ProductName TEXT,Quantity REAL,UnitPrice REAL,Total REAL);
CREATE TABLE IF NOT EXISTS Payments(Id TEXT PRIMARY KEY,Date TEXT,Type INTEGER,PartyId TEXT,PartyName TEXT,Amount REAL,Description TEXT);
CREATE TABLE IF NOT EXISTS JournalEntries(Id TEXT PRIMARY KEY,Date TEXT,Description TEXT,Reference TEXT);
CREATE TABLE IF NOT EXISTS JournalLines(Id INTEGER PRIMARY KEY AUTOINCREMENT,EntryId TEXT,Account TEXT,Debit REAL,Credit REAL);
";
        cmd.ExecuteNonQuery();

        AddColumn(c, "Invoices", "Discount", "REAL NOT NULL DEFAULT 0");
        AddColumn(c, "Invoices", "FeesAmount", "REAL NOT NULL DEFAULT 0");
        AddColumn(c, "Invoices", "FeesNote", "TEXT NOT NULL DEFAULT ''");
        AddColumn(c, "Invoices", "PaidAmount", "REAL NOT NULL DEFAULT 0");
        AddColumn(c, "Invoices", "PaymentMethod", "TEXT NOT NULL DEFAULT 'CASH'");
        AddColumn(c, "Invoices", "Currency", "TEXT NOT NULL DEFAULT 'SYP'");
        AddColumn(c, "Invoices", "Notes", "TEXT NOT NULL DEFAULT ''");
        AddColumn(c, "Invoices", "Status", "TEXT NOT NULL DEFAULT 'CLOSED'");
        AddColumn(c, "InvoiceLines", "UnitCost", "REAL NOT NULL DEFAULT 0");
        AddColumn(c, "Payments", "InvoiceId", "TEXT");
        AddColumn(c, "Payments", "Currency", "TEXT NOT NULL DEFAULT 'SYP'");
        AddColumn(c, "Payments", "FundId", "TEXT NOT NULL DEFAULT 'MAIN-SYP'");
        AddColumn(c, "Payments", "Cancelled", "INTEGER NOT NULL DEFAULT 0");
        using var extra = c.CreateCommand();
        extra.CommandText = @"
CREATE TABLE IF NOT EXISTS StockMovements(Id TEXT PRIMARY KEY,ProductId TEXT,ProductName TEXT,Type TEXT,QuantityChange REAL,QuantityBefore REAL,QuantityAfter REAL,UnitCost REAL,UnitPrice REAL,ReferenceId TEXT,Date TEXT,Notes TEXT);
CREATE INDEX IF NOT EXISTS idx_stock_product_date ON StockMovements(ProductId,Date);
CREATE TABLE IF NOT EXISTS CashFunds(Id TEXT PRIMARY KEY,Name TEXT NOT NULL,Currency TEXT NOT NULL,OpeningBalance REAL NOT NULL DEFAULT 0,IsActive INTEGER NOT NULL DEFAULT 1);
CREATE TABLE IF NOT EXISTS CashTransactions(Id TEXT PRIMARY KEY,Date TEXT NOT NULL,FundId TEXT NOT NULL,Type TEXT NOT NULL,Amount REAL NOT NULL,Currency TEXT NOT NULL,Description TEXT,ReferenceId TEXT,PartyId TEXT,IsCancelled INTEGER NOT NULL DEFAULT 0);
CREATE INDEX IF NOT EXISTS idx_cash_tx_date ON CashTransactions(Date);
CREATE TABLE IF NOT EXISTS Accounts(Id TEXT PRIMARY KEY,Code TEXT UNIQUE,Name TEXT NOT NULL,Type TEXT NOT NULL,Currency TEXT);
";
        extra.ExecuteNonQuery();
        using var phase6 = c.CreateCommand();
        phase6.CommandText = @"
CREATE TABLE IF NOT EXISTS InventoryAudits(Id TEXT PRIMARY KEY,Date TEXT NOT NULL,Number TEXT NOT NULL,Status TEXT NOT NULL,Notes TEXT);
CREATE TABLE IF NOT EXISTS InventoryAuditLines(Id INTEGER PRIMARY KEY AUTOINCREMENT,AuditId TEXT NOT NULL,ProductId TEXT NOT NULL,ProductName TEXT NOT NULL,SystemQuantity REAL NOT NULL,CountedQuantity REAL NOT NULL,Difference REAL NOT NULL,UnitCost REAL NOT NULL,ValueDifference REAL NOT NULL,Reason TEXT);
CREATE TABLE IF NOT EXISTS CashTransfers(Id TEXT PRIMARY KEY,Date TEXT NOT NULL,FromFundId TEXT NOT NULL,ToFundId TEXT NOT NULL,Amount REAL NOT NULL,FromCurrency TEXT NOT NULL,ToCurrency TEXT NOT NULL,Rate REAL NOT NULL,TargetAmount REAL NOT NULL,Notes TEXT);
CREATE TABLE IF NOT EXISTS Users(Id TEXT PRIMARY KEY,Name TEXT NOT NULL UNIQUE,Role TEXT NOT NULL,PinHash TEXT,IsActive INTEGER NOT NULL DEFAULT 1,CreatedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS AuditLogs(Id TEXT PRIMARY KEY,Action TEXT NOT NULL,EntityType TEXT NOT NULL,EntityId TEXT,Description TEXT,PerformedBy TEXT,Timestamp TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS CostLayers(Id INTEGER PRIMARY KEY AUTOINCREMENT,ProductId TEXT NOT NULL,CreatedAt TEXT NOT NULL,ReferenceId TEXT,ReferenceNumber TEXT,QuantityIn REAL NOT NULL,QuantityRemaining REAL NOT NULL,UnitCost REAL NOT NULL,Currency TEXT NOT NULL DEFAULT 'SYP');
CREATE INDEX IF NOT EXISTS idx_cost_layers_product ON CostLayers(ProductId,Id);
CREATE INDEX IF NOT EXISTS idx_audit_lines_audit ON InventoryAuditLines(AuditId);
CREATE INDEX IF NOT EXISTS idx_audit_logs_time ON AuditLogs(Timestamp);
";
        phase6.ExecuteNonQuery();
        SeedPhase6Data(c);
        SeedBigModuleData(c);

        // Backfill legacy invoices: old non-credit invoices were fully paid.
        using var backfill = c.CreateCommand();
        backfill.CommandText = @"UPDATE Invoices SET PaidAmount=CASE WHEN IsCredit=1 THEN 0 ELSE Total END,
PaymentMethod=CASE WHEN IsCredit=1 THEN 'DEFERRED' ELSE 'CASH' END WHERE PaidAmount=0 AND Total<>0";
        backfill.ExecuteNonQuery();
    }

    private static void AddColumn(SqliteConnection c, string table, string column, string definition)
    {
        // Do not swallow every SQLite error code 1: it can mean "no such table",
        // which previously let startup continue until LoadAll failed much later.
        using (var checkTable = c.CreateCommand())
        {
            checkTable.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";
            checkTable.Parameters.AddWithValue("$table", table);
            if (Convert.ToInt32(checkTable.ExecuteScalar()) == 0)
                throw new InvalidOperationException($"Required database table '{table}' was not created.");
        }

        using (var columns = c.CreateCommand())
        {
            columns.CommandText = $"PRAGMA table_info([{table}])";
            using var reader = columns.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }

        using var alter = c.CreateCommand();
        alter.CommandText = $"ALTER TABLE [{table}] ADD COLUMN [{column}] {definition}";
        alter.ExecuteNonQuery();
    }

    private static void SeedBigModuleData(SqliteConnection c)
    {
        using var q=c.CreateCommand();
        q.CommandText=@"INSERT OR IGNORE INTO CashFunds(Id,Name,Currency) VALUES('MAIN-SYP','الصندوق الرئيسي','SYP'),('MAIN-USD','الصندوق الرئيسي - دولار','USD');
INSERT OR IGNORE INTO Accounts(Id,Code,Name,Type,Currency) VALUES
('cash-syp','1111','الصندوق - ليرة سورية','ASSET','SYP'),
('cash-usd','1112','الصندوق - دولار','ASSET','USD'),
('customers','1211','العملاء','ASSET','SYP'),
('inventory','1311','المخزون','ASSET','SYP'),
('suppliers','2111','الموردون','LIABILITY','SYP'),
('sales','4111','المبيعات','REVENUE','SYP'),
('purchases','5111','المشتريات','EXPENSE','SYP'),
('expenses','5211','المصروفات','EXPENSE','SYP');";
        q.ExecuteNonQuery();
    }

    public IReadOnlyList<(string Id,string Name,string Currency,decimal Balance)> CashFunds()
    {
        using var c=Open(); using var q=c.CreateCommand(); q.CommandText="SELECT f.Id,f.Name,f.Currency,f.OpeningBalance + COALESCE((SELECT SUM(CASE WHEN t.Type IN ('IN','TRANSFER_IN') THEN t.Amount ELSE -t.Amount END) FROM CashTransactions t WHERE t.FundId=f.Id AND t.IsCancelled=0),0) FROM CashFunds f WHERE f.IsActive=1 ORDER BY f.Currency";
        using var r=q.ExecuteReader(); var list=new List<(string,string,string,decimal)>(); while(r.Read()) list.Add((r.GetString(0),r.GetString(1),r.GetString(2),(decimal)r.GetDouble(3))); return list;
    }

    public void AddCashTransaction(string fundId,string type,decimal amount,string currency,string description,string? referenceId=null,Guid? partyId=null)
    {
        if(amount<=0) throw new InvalidOperationException("قيمة الحركة يجب أن تكون أكبر من صفر.");
        if(type is not ("IN" or "OUT")) throw new InvalidOperationException("نوع الحركة النقدية غير صالح.");
        using var c=Open(); using var tx=c.BeginTransaction();
        void Exec(string sql, Action<SqliteCommand> fill) { using var q=c.CreateCommand(); q.Transaction=tx; q.CommandText=sql; fill(q); q.ExecuteNonQuery(); }
        var txId=Guid.NewGuid().ToString();
        Exec("INSERT INTO CashTransactions(Id,Date,FundId,Type,Amount,Currency,Description,ReferenceId,PartyId) VALUES($id,$d,$f,$t,$a,$c,$x,$r,$p)", q => {
            q.Parameters.AddWithValue("$id",txId); q.Parameters.AddWithValue("$d",DateTime.Now.ToString("O")); q.Parameters.AddWithValue("$f",fundId); q.Parameters.AddWithValue("$t",type); q.Parameters.AddWithValue("$a",(double)amount); q.Parameters.AddWithValue("$c",currency); q.Parameters.AddWithValue("$x",description); q.Parameters.AddWithValue("$r",referenceId??""); q.Parameters.AddWithValue("$p",partyId?.ToString()??"");
        });
        var eid=Guid.NewGuid().ToString();
        Exec("INSERT INTO JournalEntries VALUES($id,$date,$desc,$ref)", q => { q.Parameters.AddWithValue("$id",eid); q.Parameters.AddWithValue("$date",DateTime.Now.ToString("O")); q.Parameters.AddWithValue("$desc",type=="IN"?"سند قبض نقدي":"سند صرف نقدي"); q.Parameters.AddWithValue("$ref",txId); });
        var cash=CashedAccount(currency);
        var debit=type=="IN"?cash:"المصروفات";
        var credit=type=="IN"?"إيرادات أخرى":cash;
        Exec("INSERT INTO JournalLines(EntryId,Account,Debit,Credit) VALUES($e,$a,$d,0),($e,$b,0,$c)", q=>{q.Parameters.AddWithValue("$e",eid);q.Parameters.AddWithValue("$a",debit);q.Parameters.AddWithValue("$b",credit);q.Parameters.AddWithValue("$d",(double)amount);q.Parameters.AddWithValue("$c",(double)amount);});
        tx.Commit(); LoadAll(); Log("CASH_TX","CASH",txId,$"{(type=="IN"?"قبض":"صرف")} {amount:N2} {currency}: {description}");
    }

    public IReadOnlyList<(DateTime Date,string Fund,string Type,decimal Amount,string Currency,string Description)> CashTransactions(string? fundId=null)
    {
        using var c=Open(); using var q=c.CreateCommand(); q.CommandText="SELECT Date,FundId,Type,Amount,Currency,Description FROM CashTransactions WHERE IsCancelled=0 AND ($f='' OR FundId=$f) ORDER BY Date DESC"; q.Parameters.AddWithValue("$f",fundId??""); using var r=q.ExecuteReader(); var list=new List<(DateTime,string,string,decimal,string,string)>(); while(r.Read()) list.Add((DateTime.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),(decimal)r.GetDouble(3),r.GetString(4),r.GetString(5))); return list;
    }

    public IReadOnlyList<(string Code,string Name,string Type,string Currency)> Accounts()
    {
        using var c=Open(); using var q=c.CreateCommand(); q.CommandText="SELECT COALESCE(Code,''),Name,Type,COALESCE(Currency,'') FROM Accounts ORDER BY Code";
        using var r=q.ExecuteReader(); var list=new List<(string,string,string,string)>();
        while(r.Read()) list.Add((r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3)));
        return list;
    }

    public IReadOnlyList<(string Code,string Name,string Type,decimal Debit,decimal Credit)> TrialBalance()
    {
        using var c=Open(); using var q=c.CreateCommand(); q.CommandText="SELECT Account, SUM(Debit), SUM(Credit) FROM JournalLines GROUP BY Account ORDER BY Account"; using var r=q.ExecuteReader(); var list=new List<(string,string,string,decimal,decimal)>(); while(r.Read()){var a=r.GetString(0);list.Add((a,a,"",(decimal)r.GetDouble(1),(decimal)r.GetDouble(2)));} return list;
    }

    public IReadOnlyList<(string Product,decimal Quantity,decimal Cost,decimal Value)> InventoryReport()
        => Products.Select(p=>(Product:p.Name,Quantity:p.Quantity,Cost:p.PurchasePrice,Value:p.Quantity*p.PurchasePrice)).OrderBy(x=>x.Product).ToList();

    public void LoadAll()
    {
        Products = [];
        Parties = [];
        Invoices = [];
        Payments = [];
        JournalEntries = [];

        using var c = Open();

        using (var q = c.CreateCommand())
        {
            q.CommandText = "SELECT Id,Code,Barcode,Name,Unit,PurchasePrice,SalePrice,Quantity,MinQuantity,IsActive FROM Products";
            using var r = q.ExecuteReader();
            while (r.Read())
                Products.Add(new Product { Id = Guid.Parse(r.GetString(0)), Code = r.GetString(1), Barcode = r.GetString(2), Name = r.GetString(3), Unit = r.GetString(4), PurchasePrice = (decimal)r.GetDouble(5), SalePrice = (decimal)r.GetDouble(6), Quantity = (decimal)r.GetDouble(7), MinQuantity = (decimal)r.GetDouble(8), IsActive = r.GetInt32(9) != 0 });
        }

        using (var q = c.CreateCommand())
        {
            q.CommandText = "SELECT Id,Name,Phone,Address,Type,Balance FROM Parties ORDER BY Name";
            using var r = q.ExecuteReader();
            while (r.Read())
                Parties.Add(new Party { Id = Guid.Parse(r.GetString(0)), Name = r.GetString(1), Phone = r.GetString(2), Address = r.GetString(3), Type = (PartyType)r.GetInt32(4), Balance = (decimal)r.GetDouble(5) });
        }

        using (var q = c.CreateCommand())
        {
            q.CommandText = "SELECT Id,Number,Date,Type,PartyId,PartyName,Discount,FeesAmount,FeesNote,PaidAmount,PaymentMethod,Currency,Notes,Status,IsCredit FROM Invoices ORDER BY Date DESC";
            using var r = q.ExecuteReader();
            while (r.Read())
            {
                var inv = new Invoice
                {
                    Id = Guid.Parse(r.GetString(0)), Number = r.GetString(1), Date = DateTime.Parse(r.GetString(2)), Type = (InvoiceType)r.GetInt32(3),
                    PartyId = string.IsNullOrEmpty(r.GetString(4)) ? null : Guid.Parse(r.GetString(4)), PartyName = r.GetString(5), Discount = (decimal)r.GetDouble(6),
                    FeesAmount = (decimal)r.GetDouble(7), FeesNote = r.GetString(8), PaidAmount = (decimal)r.GetDouble(9), PaymentMethod = r.GetString(10),
                    Currency = r.GetString(11), Notes = r.GetString(12), Status = r.GetString(13), IsCredit = r.GetInt32(14) != 0
                };
                Invoices.Add(inv);
            }
        }

        foreach (var inv in Invoices)
        {
            using var q = c.CreateCommand();
            q.CommandText = "SELECT ProductId,ProductName,Quantity,UnitPrice,UnitCost FROM InvoiceLines WHERE InvoiceId=$id ORDER BY Id";
            q.Parameters.AddWithValue("$id", inv.Id.ToString());
            using var r = q.ExecuteReader();
            while (r.Read()) inv.Lines.Add(new InvoiceLine { ProductId = Guid.Parse(r.GetString(0)), ProductName = r.GetString(1), Quantity = (decimal)r.GetDouble(2), UnitPrice = (decimal)r.GetDouble(3), UnitCost = (decimal)r.GetDouble(4) });
        }

        using (var q = c.CreateCommand())
        {
            q.CommandText = "SELECT Id,Date,Description,Reference FROM JournalEntries ORDER BY Date DESC";
            using var r = q.ExecuteReader();
            while (r.Read())
            {
                var e = new JournalEntry { Id = Guid.Parse(r.GetString(0)), Date = DateTime.Parse(r.GetString(1)), Description = r.GetString(2), Reference = r.GetString(3) };
                using var lq = c.CreateCommand();
                lq.CommandText = "SELECT Account,Debit,Credit FROM JournalLines WHERE EntryId=$id";
                lq.Parameters.AddWithValue("$id", e.Id.ToString());
                using var lr = lq.ExecuteReader();
                while (lr.Read()) e.Lines.Add(new JournalLine { Account = lr.GetString(0), Debit = (decimal)lr.GetDouble(1), Credit = (decimal)lr.GetDouble(2) });
                JournalEntries.Add(e);
            }
        }

        using (var q = c.CreateCommand())
        {
            q.CommandText = "SELECT Id,Date,Type,PartyId,PartyName,InvoiceId,Amount,PaymentMethod,Description FROM Payments ORDER BY Date DESC";
            using var r = q.ExecuteReader();
            while (r.Read()) Payments.Add(new Payment { Id = Guid.Parse(r.GetString(0)), Date = DateTime.Parse(r.GetString(1)), Type = (PaymentType)r.GetInt32(2), PartyId = string.IsNullOrEmpty(r.GetString(3)) ? null : Guid.Parse(r.GetString(3)), PartyName = r.GetString(4), InvoiceId = string.IsNullOrEmpty(r.GetString(5)) ? null : Guid.Parse(r.GetString(5)), Amount = (decimal)r.GetDouble(6), PaymentMethod = r.GetString(7), Description = r.GetString(8) });
        }
    }

    public void SaveProduct(Product p)
    {
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = "INSERT INTO Products VALUES($id,$code,$bar,$name,$unit,$pp,$sp,$qty,$min,$a) ON CONFLICT(Id) DO UPDATE SET Code=$code,Barcode=$bar,Name=$name,Unit=$unit,PurchasePrice=$pp,SalePrice=$sp,Quantity=$qty,MinQuantity=$min,IsActive=$a";
        q.Parameters.AddWithValue("$id", p.Id.ToString()); q.Parameters.AddWithValue("$code", p.Code); q.Parameters.AddWithValue("$bar", p.Barcode); q.Parameters.AddWithValue("$name", p.Name); q.Parameters.AddWithValue("$unit", p.Unit); q.Parameters.AddWithValue("$pp", (double)p.PurchasePrice); q.Parameters.AddWithValue("$sp", (double)p.SalePrice); q.Parameters.AddWithValue("$qty", (double)p.Quantity); q.Parameters.AddWithValue("$min", (double)p.MinQuantity); q.Parameters.AddWithValue("$a", p.IsActive ? 1 : 0); q.ExecuteNonQuery(); LoadAll();
    }

    public void DeleteProduct(Guid id)
    {
        using var c=Open(); using var tx=c.BeginTransaction();
        using var q=c.CreateCommand(); q.Transaction=tx; q.CommandText="SELECT COUNT(*) FROM InvoiceLines WHERE ProductId=$id"; q.Parameters.AddWithValue("$id",id.ToString()); var referenced=Convert.ToInt32(q.ExecuteScalar())>0;
        using var u=c.CreateCommand(); u.Transaction=tx; u.CommandText=referenced?"UPDATE Products SET IsActive=0 WHERE Id=$id":"DELETE FROM Products WHERE Id=$id"; u.Parameters.AddWithValue("$id",id.ToString()); u.ExecuteNonQuery(); tx.Commit(); LoadAll();
        Log(referenced?"PRODUCT_ARCHIVE":"PRODUCT_DELETE","PRODUCT",id.ToString(),referenced?"تم أرشفة الصنف لأنه مستخدم في فواتير":"تم حذف الصنف");
    }

    public void SaveParty(Party p)
    {
        using var c = Open(); using var q = c.CreateCommand(); q.CommandText = "INSERT INTO Parties VALUES($id,$name,$phone,$addr,$type,$bal) ON CONFLICT(Id) DO UPDATE SET Name=$name,Phone=$phone,Address=$addr,Type=$type,Balance=$bal";
        q.Parameters.AddWithValue("$id", p.Id.ToString()); q.Parameters.AddWithValue("$name", p.Name); q.Parameters.AddWithValue("$phone", p.Phone); q.Parameters.AddWithValue("$addr", p.Address); q.Parameters.AddWithValue("$type", (int)p.Type); q.Parameters.AddWithValue("$bal", (double)p.Balance); q.ExecuteNonQuery(); LoadAll();
    }

    public void SaveInvoice(Invoice x)
    {
        if (x.IsQuotation) { SaveQuotation(x); return; }
        if (x.Lines.Count == 0) throw new InvalidOperationException("الفاتورة لا تحتوي على أصناف.");
        if (x.Lines.Any(l => l.Quantity <= 0 || l.UnitPrice < 0)) throw new InvalidOperationException("يوجد سطر بكمية أو سعر غير صالح.");
        if (x.Discount < 0 || x.FeesAmount < 0) throw new InvalidOperationException("الخصم والأجور الإضافية لا يمكن أن تكون سالبة.");
        x.PaidAmount = Math.Clamp(x.PaidAmount, 0, x.Total);
        x.IsCredit = x.PaidAmount < x.Total - 0.0001m;

        using var c = Open(); using var tx = c.BeginTransaction();
        foreach (var l in x.Lines)
        {
            var p = Products.FirstOrDefault(z => z.Id == l.ProductId) ?? throw new InvalidOperationException("المنتج غير موجود");
            l.UnitCost = p.PurchasePrice;
            if (x.Type is InvoiceType.Sale or InvoiceType.PurchaseReturn)
                l.UnitCost = PreviewFifoCost(c, tx, p.Id, l.Quantity, p.PurchasePrice);
            var delta = StockDelta(x.Type, l.Quantity);
            if (p.Quantity + delta < 0) throw new InvalidOperationException($"المخزون غير كافٍ للمنتج: {p.Name}");
        }

        void Exec(string sql, Action<SqliteCommand> fill)
        {
            using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql; fill(q); q.ExecuteNonQuery();
        }

        Exec(@"INSERT INTO Invoices(Id,Number,Date,Type,PartyId,PartyName,Total,IsCredit,Discount,FeesAmount,FeesNote,PaidAmount,PaymentMethod,Currency,Notes,Status)
VALUES($id,$no,$date,$type,$party,$name,$total,$credit,$discount,$fees,$feesnote,$paid,$method,$currency,$notes,$status)", q =>
        {
            q.Parameters.AddWithValue("$id", x.Id.ToString()); q.Parameters.AddWithValue("$no", x.Number); q.Parameters.AddWithValue("$date", x.Date.ToString("O")); q.Parameters.AddWithValue("$type", (int)x.Type); q.Parameters.AddWithValue("$party", x.PartyId?.ToString() ?? ""); q.Parameters.AddWithValue("$name", x.PartyName); q.Parameters.AddWithValue("$total", (double)x.Total); q.Parameters.AddWithValue("$credit", x.IsCredit ? 1 : 0); q.Parameters.AddWithValue("$discount", (double)x.Discount); q.Parameters.AddWithValue("$fees", (double)x.FeesAmount); q.Parameters.AddWithValue("$feesnote", x.FeesNote); q.Parameters.AddWithValue("$paid", (double)x.PaidAmount); q.Parameters.AddWithValue("$method", x.PaymentMethod); q.Parameters.AddWithValue("$currency", x.Currency); q.Parameters.AddWithValue("$notes", x.Notes); q.Parameters.AddWithValue("$status", x.Status);
        });

        foreach (var l in x.Lines)
        {
            Exec("INSERT INTO InvoiceLines(InvoiceId,ProductId,ProductName,Quantity,UnitPrice,Total,UnitCost) VALUES($i,$p,$n,$q,$u,$t,$cost)", q => { q.Parameters.AddWithValue("$i", x.Id.ToString()); q.Parameters.AddWithValue("$p", l.ProductId.ToString()); q.Parameters.AddWithValue("$n", l.ProductName); q.Parameters.AddWithValue("$q", (double)l.Quantity); q.Parameters.AddWithValue("$u", (double)l.UnitPrice); q.Parameters.AddWithValue("$t", (double)l.Total); q.Parameters.AddWithValue("$cost", (double)l.UnitCost); });
            var d = StockDelta(x.Type, l.Quantity);
            var beforeQty = Products.First(z => z.Id == l.ProductId).Quantity;
            var afterQty = beforeQty + d;
            Exec("UPDATE Products SET Quantity=Quantity+$d WHERE Id=$id", q => { q.Parameters.AddWithValue("$d", (double)d); q.Parameters.AddWithValue("$id", l.ProductId.ToString()); });
            Exec("INSERT INTO StockMovements(Id,ProductId,ProductName,Type,QuantityChange,QuantityBefore,QuantityAfter,UnitCost,UnitPrice,ReferenceId,Date,Notes) VALUES($id,$p,$n,$t,$d,$b,$a,$c,$u,$r,$dt,$x)", q => { q.Parameters.AddWithValue("$id",Guid.NewGuid().ToString()); q.Parameters.AddWithValue("$p",l.ProductId.ToString()); q.Parameters.AddWithValue("$n",l.ProductName); q.Parameters.AddWithValue("$t",x.Type.ToString()); q.Parameters.AddWithValue("$d",(double)d); q.Parameters.AddWithValue("$b",(double)beforeQty); q.Parameters.AddWithValue("$a",(double)afterQty); q.Parameters.AddWithValue("$c",(double)l.UnitCost); q.Parameters.AddWithValue("$u",(double)l.UnitPrice); q.Parameters.AddWithValue("$r",x.Id.ToString()); q.Parameters.AddWithValue("$dt",x.Date.ToString("O")); q.Parameters.AddWithValue("$x",x.Number); });
        }

        foreach (var l in x.Lines)
        {
            if (x.Type is InvoiceType.Purchase or InvoiceType.SaleReturn)
            {
                Exec("INSERT INTO CostLayers(ProductId,CreatedAt,ReferenceId,ReferenceNumber,QuantityIn,QuantityRemaining,UnitCost,Currency) VALUES($p,$d,$r,$n,$q,$q,$c,$cur)",q=>{q.Parameters.AddWithValue("$p",l.ProductId.ToString());q.Parameters.AddWithValue("$d",x.Date.ToString("O"));q.Parameters.AddWithValue("$r",x.Id.ToString());q.Parameters.AddWithValue("$n",x.Number);q.Parameters.AddWithValue("$q",(double)l.Quantity);q.Parameters.AddWithValue("$c",(double)l.UnitCost);q.Parameters.AddWithValue("$cur",x.Currency);});
            }
            else if (x.Type is InvoiceType.Sale or InvoiceType.PurchaseReturn)
            {
                ConsumeFifoLayers(c,tx,l.ProductId,l.Quantity);
            }
        }

        var outstanding = x.Remaining;
        if (x.PartyId.HasValue && outstanding > 0)
        {
            var sign = x.Type switch { InvoiceType.Sale => 1m, InvoiceType.Purchase => -1m, InvoiceType.SaleReturn => -1m, InvoiceType.PurchaseReturn => 1m, _ => 0m };
            if (sign != 0) Exec("UPDATE Parties SET Balance=Balance+$d WHERE Id=$id", q => { q.Parameters.AddWithValue("$d", (double)(outstanding * sign)); q.Parameters.AddWithValue("$id", x.PartyId.Value.ToString()); });
        }

        CreateInvoiceJournal(c, tx, x);
        if (x.PaidAmount > 0 && x.PaymentMethod != PaymentMethods.Deferred)
        {
            InsertInvoicePayment(c, tx, x);
            if (x.PaymentMethod == PaymentMethods.Cash)
            {
                var cashType = x.Type is InvoiceType.Sale or InvoiceType.PurchaseReturn ? "IN" : "OUT";
                Exec("INSERT INTO CashTransactions(Id,Date,FundId,Type,Amount,Currency,Description,ReferenceId,PartyId) VALUES($id,$d,$f,$t,$a,$c,$x,$r,$p)", q => { q.Parameters.AddWithValue("$id",Guid.NewGuid().ToString());q.Parameters.AddWithValue("$d",x.Date.ToString("O"));q.Parameters.AddWithValue("$f",x.Currency=="USD"?"MAIN-USD":"MAIN-SYP");q.Parameters.AddWithValue("$t",cashType);q.Parameters.AddWithValue("$a",(double)x.PaidAmount);q.Parameters.AddWithValue("$c",x.Currency);q.Parameters.AddWithValue("$x",$"حركة نقدية من {x.Number}");q.Parameters.AddWithValue("$r",x.Id.ToString());q.Parameters.AddWithValue("$p",x.PartyId?.ToString()??""); });
            }
        }

        tx.Commit(); LoadAll();
    }

    private void SaveQuotation(Invoice x)
    {
        if (x.Lines.Count == 0) throw new InvalidOperationException("عرض السعر لا يحتوي على أصناف.");
        using var c = Open(); using var tx = c.BeginTransaction();
        x.Status = "CLOSED"; x.PaidAmount = 0; x.IsCredit = true;
        void Exec(string sql, Action<SqliteCommand> fill) { using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql; fill(q); q.ExecuteNonQuery(); }
        Exec(@"INSERT INTO Invoices(Id,Number,Date,Type,PartyId,PartyName,Total,IsCredit,Discount,FeesAmount,FeesNote,PaidAmount,PaymentMethod,Currency,Notes,Status)
VALUES($id,$no,$date,$type,$party,$name,$total,1,$discount,$fees,$feesnote,0,'DEFERRED',$currency,$notes,'CLOSED')", q => { q.Parameters.AddWithValue("$id", x.Id.ToString()); q.Parameters.AddWithValue("$no", x.Number); q.Parameters.AddWithValue("$date", x.Date.ToString("O")); q.Parameters.AddWithValue("$type", (int)x.Type); q.Parameters.AddWithValue("$party", x.PartyId?.ToString() ?? ""); q.Parameters.AddWithValue("$name", x.PartyName); q.Parameters.AddWithValue("$total", (double)x.Total); q.Parameters.AddWithValue("$discount", (double)x.Discount); q.Parameters.AddWithValue("$fees", (double)x.FeesAmount); q.Parameters.AddWithValue("$feesnote", x.FeesNote); q.Parameters.AddWithValue("$currency", x.Currency); q.Parameters.AddWithValue("$notes", x.Notes); });
        foreach (var l in x.Lines) Exec("INSERT INTO InvoiceLines(InvoiceId,ProductId,ProductName,Quantity,UnitPrice,Total,UnitCost) VALUES($i,$p,$n,$q,$u,$t,0)", q => { q.Parameters.AddWithValue("$i", x.Id.ToString()); q.Parameters.AddWithValue("$p", l.ProductId.ToString()); q.Parameters.AddWithValue("$n", l.ProductName); q.Parameters.AddWithValue("$q", (double)l.Quantity); q.Parameters.AddWithValue("$u", (double)l.UnitPrice); q.Parameters.AddWithValue("$t", (double)l.Total); });
        tx.Commit(); LoadAll();
    }

    private static decimal StockDelta(InvoiceType type, decimal quantity) => type switch
    {
        InvoiceType.Purchase => quantity,
        InvoiceType.Sale => -quantity,
        InvoiceType.SaleReturn => quantity,
        InvoiceType.PurchaseReturn => -quantity,
        _ => 0
    };

    private static string CashedAccount(string currency) => currency == "USD" ? "الصندوق - دولار" : "الصندوق - ليرة سورية";
    private static string SettlementAccount(string paymentMethod, string currency) => paymentMethod switch
    {
        PaymentMethods.BankTransfer => "البنك",
        PaymentMethods.Card => "بطاقات بنكية",
        PaymentMethods.ShamCash => "شام كاش",
        PaymentMethods.Cheque => "شيكات",
        _ => CashedAccount(currency)
    };

    private static void CreateInvoiceJournal(SqliteConnection c, SqliteTransaction tx, Invoice x)
    {
        if (x.IsQuotation) return;
        void Exec(string sql, Action<SqliteCommand> fill) { using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql; fill(q); q.ExecuteNonQuery(); }
        var eid = Guid.NewGuid().ToString();
        Exec("INSERT INTO JournalEntries VALUES($id,$date,$desc,$ref)", q => { q.Parameters.AddWithValue("$id", eid); q.Parameters.AddWithValue("$date", x.Date.ToString("O")); q.Parameters.AddWithValue("$desc", TypeName(x.Type)); q.Parameters.AddWithValue("$ref", x.Number); });

        void Line(string account, decimal debit, decimal credit)
        {
            if (debit <= 0 && credit <= 0) return;
            Exec("INSERT INTO JournalLines(EntryId,Account,Debit,Credit) VALUES($e,$a,$d,$c)", q => { q.Parameters.AddWithValue("$e", eid); q.Parameters.AddWithValue("$a", account); q.Parameters.AddWithValue("$d", (double)debit); q.Parameters.AddWithValue("$c", (double)credit); });
        }

        var total = x.Total;
        var paid = x.PaymentMethod == PaymentMethods.Deferred ? 0 : x.PaidAmount;
        var remaining = Math.Max(0, total - paid);
        var cost = x.CostOfGoodsSold;
        var cashAccount = SettlementAccount(x.PaymentMethod, x.Currency);

        switch (x.Type)
        {
            case InvoiceType.Sale:
                Line(cashAccount, paid, 0);
                Line("العملاء", remaining, 0);
                Line("المبيعات", 0, total);
                Line("تكلفة البضاعة المباعة", cost, 0);
                Line("المخزون", 0, cost);
                break;
            case InvoiceType.Purchase:
                Line("المخزون", total, 0);
                Line(cashAccount, 0, paid);
                Line("الموردون", 0, remaining);
                break;
            case InvoiceType.SaleReturn:
                Line("مرتجعات المبيعات", total, 0);
                Line(cashAccount, 0, paid);
                Line("العملاء", 0, remaining);
                Line("المخزون", cost, 0);
                Line("تكلفة البضاعة المباعة", 0, cost);
                break;
            case InvoiceType.PurchaseReturn:
                Line(cashAccount, paid, 0);
                Line("الموردون", remaining, 0);
                Line("المخزون", 0, total);
                break;
        }

        using var check = c.CreateCommand();
        check.Transaction = tx;
        check.CommandText = "SELECT ABS(COALESCE(SUM(Debit),0)-COALESCE(SUM(Credit),0)) FROM JournalLines WHERE EntryId=$e";
        check.Parameters.AddWithValue("$e", eid);
        var difference = Convert.ToDecimal(check.ExecuteScalar() ?? 0, System.Globalization.CultureInfo.InvariantCulture);
        if (difference > 0.01m) throw new InvalidOperationException($"القيد المحاسبي غير متوازن، الفرق: {difference:N2}");
    }

    private static void InsertInvoicePayment(SqliteConnection c, SqliteTransaction tx, Invoice x)
    {
        var type = x.Type switch { InvoiceType.Sale or InvoiceType.PurchaseReturn => PaymentType.CustomerReceipt, _ => PaymentType.SupplierPayment };
        if (x.Type == InvoiceType.Sale) type = PaymentType.CustomerReceipt;
        if (x.Type == InvoiceType.Purchase) type = PaymentType.SupplierPayment;
        if (x.Type == InvoiceType.SaleReturn) type = PaymentType.SupplierPayment;
        if (x.Type == InvoiceType.PurchaseReturn) type = PaymentType.CustomerReceipt;
        using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = "INSERT INTO Payments(Id,Date,Type,PartyId,PartyName,InvoiceId,Amount,PaymentMethod,Description) VALUES($id,$date,$type,$party,$name,$invoice,$amount,$method,$desc)";
        q.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); q.Parameters.AddWithValue("$date", x.Date.ToString("O")); q.Parameters.AddWithValue("$type", (int)type); q.Parameters.AddWithValue("$party", x.PartyId?.ToString() ?? ""); q.Parameters.AddWithValue("$name", x.PartyName); q.Parameters.AddWithValue("$invoice", x.Id.ToString()); q.Parameters.AddWithValue("$amount", (double)x.PaidAmount); q.Parameters.AddWithValue("$method", x.PaymentMethod); q.Parameters.AddWithValue("$desc", $"دفعة مرتبطة بالفاتورة {x.Number}"); q.ExecuteNonQuery();
    }

    public void SavePayment(Payment p)
    {
        if (p.Amount <= 0) throw new InvalidOperationException("قيمة السند يجب أن تكون أكبر من صفر.");
        if ((p.Type == PaymentType.CustomerReceipt || p.Type == PaymentType.SupplierPayment) && !p.PartyId.HasValue)
            throw new InvalidOperationException("يجب اختيار العميل أو المورد.");
        using var c = Open();
        using var tx = c.BeginTransaction();
        void Exec(string sql, Action<SqliteCommand> fill) { using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql; fill(q); q.ExecuteNonQuery(); }

        string? invoiceNumber = null;
        if (p.InvoiceId.HasValue)
        {
            using var iq = c.CreateCommand();
            iq.Transaction = tx;
            iq.CommandText = "SELECT Number,Total,PaidAmount,Type,Status FROM Invoices WHERE Id=$id";
            iq.Parameters.AddWithValue("$id", p.InvoiceId.Value.ToString());
            using var ir = iq.ExecuteReader();
            if (!ir.Read()) throw new InvalidOperationException("الفاتورة المرتبطة بالسند غير موجودة.");
            invoiceNumber = ir.GetString(0);
            var invoiceTotal = (decimal)ir.GetDouble(1);
            var currentPaid = (decimal)ir.GetDouble(2);
            var nextPaid = Math.Min(invoiceTotal, currentPaid + p.Amount);
            var nextStatus = nextPaid >= invoiceTotal - 0.0001m ? "CLOSED" : "OPEN";
            // Move back before executing update because the reader is still open.
            ir.Close();
            Exec("UPDATE Invoices SET PaidAmount=$paid,IsCredit=$credit,Status=$status,PaymentMethod=$method WHERE Id=$id", q =>
            {
                q.Parameters.AddWithValue("$paid", (double)nextPaid);
                q.Parameters.AddWithValue("$credit", nextPaid < invoiceTotal - 0.0001m ? 1 : 0);
                q.Parameters.AddWithValue("$status", nextStatus);
                q.Parameters.AddWithValue("$method", p.PaymentMethod);
                q.Parameters.AddWithValue("$id", p.InvoiceId.Value.ToString());
            });
        }

        Exec("INSERT INTO Payments(Id,Date,Type,PartyId,PartyName,InvoiceId,Amount,PaymentMethod,Description) VALUES($id,$date,$type,$party,$name,$invoice,$amount,$method,$desc)", q =>
        {
            q.Parameters.AddWithValue("$id", p.Id.ToString()); q.Parameters.AddWithValue("$date", p.Date.ToString("O")); q.Parameters.AddWithValue("$type", (int)p.Type);
            q.Parameters.AddWithValue("$party", p.PartyId?.ToString() ?? ""); q.Parameters.AddWithValue("$name", p.PartyName); q.Parameters.AddWithValue("$invoice", p.InvoiceId?.ToString() ?? "");
            q.Parameters.AddWithValue("$amount", (double)p.Amount); q.Parameters.AddWithValue("$method", p.PaymentMethod); q.Parameters.AddWithValue("$desc", p.Description);
        });

        if (p.PartyId.HasValue)
        {
            var delta = p.Type == PaymentType.CustomerReceipt ? -p.Amount : p.Type == PaymentType.SupplierPayment ? p.Amount : 0m;
            if (delta != 0) Exec("UPDATE Parties SET Balance=Balance+$d WHERE Id=$id", q => { q.Parameters.AddWithValue("$d", (double)delta); q.Parameters.AddWithValue("$id", p.PartyId.Value.ToString()); });
        }

        if (p.Type is PaymentType.CustomerReceipt or PaymentType.SupplierPayment or PaymentType.CashIncome or PaymentType.CashExpense)
        {
            var cashType = p.Type is PaymentType.CustomerReceipt or PaymentType.CashIncome ? "IN" : "OUT";
            if (p.PaymentMethod == PaymentMethods.Cash)
            {
                Exec("INSERT INTO CashTransactions(Id,Date,FundId,Type,Amount,Currency,Description,ReferenceId,PartyId) VALUES($i,$d,'MAIN-SYP',$t,$a,'SYP',$x,$r,$p)", q =>
                {
                    q.Parameters.AddWithValue("$i", Guid.NewGuid().ToString()); q.Parameters.AddWithValue("$d", p.Date.ToString("O")); q.Parameters.AddWithValue("$t", cashType); q.Parameters.AddWithValue("$a", (double)p.Amount);
                    q.Parameters.AddWithValue("$x", p.Description); q.Parameters.AddWithValue("$r", p.Id.ToString()); q.Parameters.AddWithValue("$p", p.PartyId?.ToString() ?? "");
                });
            }
        }

        var eid = Guid.NewGuid().ToString();
        Exec("INSERT INTO JournalEntries VALUES($id,$date,$desc,$ref)", q => { q.Parameters.AddWithValue("$id", eid); q.Parameters.AddWithValue("$date", p.Date.ToString("O")); q.Parameters.AddWithValue("$desc", PaymentName(p.Type)); q.Parameters.AddWithValue("$ref", p.Id.ToString()); });
        var settlementAccount = p.PaymentMethod switch
        {
            PaymentMethods.BankTransfer => "البنك",
            PaymentMethods.Card => "بطاقات بنكية",
            PaymentMethods.ShamCash => "شام كاش",
            PaymentMethods.Cheque => "شيكات",
            _ => "الصندوق"
        };
        var debit = p.Type switch { PaymentType.CustomerReceipt => settlementAccount, PaymentType.SupplierPayment => "الموردون", PaymentType.CashIncome => settlementAccount, _ => "المصروفات" };
        var credit = p.Type switch { PaymentType.CustomerReceipt => "العملاء", PaymentType.SupplierPayment => settlementAccount, PaymentType.CashIncome => "إيرادات أخرى", _ => settlementAccount };
        Exec("INSERT INTO JournalLines(EntryId,Account,Debit,Credit) VALUES($e,$a,$d,0),($e,$b,0,$c)", q => { q.Parameters.AddWithValue("$e", eid); q.Parameters.AddWithValue("$a", debit); q.Parameters.AddWithValue("$b", credit); q.Parameters.AddWithValue("$d", (double)p.Amount); q.Parameters.AddWithValue("$c", (double)p.Amount); });

        tx.Commit();
        LoadAll();
        Log("PAYMENT_SAVE", "PAYMENT", p.Id.ToString(), invoiceNumber is null ? PaymentName(p.Type) : $"{PaymentName(p.Type)} — الفاتورة {invoiceNumber}", App.CurrentUser?.Name ?? "admin");
    }

    private static string TypeName(InvoiceType t) => t switch { InvoiceType.Sale => "قيد مبيعات", InvoiceType.Purchase => "قيد مشتريات", InvoiceType.SaleReturn => "مرتجع مبيعات", InvoiceType.PurchaseReturn => "مرتجع مشتريات", _ => "عرض أسعار" };
    private static string PaymentName(PaymentType t) => t switch { PaymentType.CustomerReceipt => "سند قبض من عميل", PaymentType.SupplierPayment => "سند دفع لمورد", PaymentType.CashIncome => "إيراد نقدي", _ => "مصروف نقدي" };

    public decimal TodaySales => Invoices.Where(x => x.Date.Date == DateTime.Today && x.Type == InvoiceType.Sale).Sum(x => x.Total);
    public decimal TodayPurchases => Invoices.Where(x => x.Date.Date == DateTime.Today && x.Type == InvoiceType.Purchase).Sum(x => x.Total);
    public decimal InventoryValue => Products.Sum(x => x.Quantity * x.PurchasePrice);
    public decimal CashBalance => CashFunds().Where(x => x.Currency == "SYP").Sum(x => x.Balance);
    public decimal CashBalanceUsd => CashFunds().Where(x => x.Currency == "USD").Sum(x => x.Balance);

    public void EnsureExchangeRates(){ using var c=Open(); using var q=c.CreateCommand(); q.CommandText="CREATE TABLE IF NOT EXISTS ExchangeRates(Currency TEXT PRIMARY KEY,Rate REAL NOT NULL,UpdatedAt TEXT NOT NULL)"; q.ExecuteNonQuery(); }
    public List<MizanDesktop.Models.ExchangeRate> GetExchangeRates(){ EnsureExchangeRates(); using var c=Open(); using var q=c.CreateCommand(); q.CommandText="SELECT Currency,Rate,UpdatedAt FROM ExchangeRates ORDER BY Currency"; using var r=q.ExecuteReader(); var list=new List<MizanDesktop.Models.ExchangeRate>(); while(r.Read()) list.Add(new MizanDesktop.Models.ExchangeRate{Currency=r.GetString(0),Rate=(decimal)r.GetDouble(1),UpdatedAt=DateTime.Parse(r.GetString(2))}); return list; }
    public void SaveExchangeRate(MizanDesktop.Models.ExchangeRate x){ if(x.Rate<=0) throw new InvalidOperationException("سعر الصرف يجب أن يكون أكبر من صفر."); EnsureExchangeRates(); using var c=Open(); using var q=c.CreateCommand(); q.CommandText="INSERT INTO ExchangeRates(Currency,Rate,UpdatedAt) VALUES($c,$r,$d) ON CONFLICT(Currency) DO UPDATE SET Rate=$r,UpdatedAt=$d"; q.Parameters.AddWithValue("$c",x.Currency); q.Parameters.AddWithValue("$r",(double)x.Rate); q.Parameters.AddWithValue("$d",x.UpdatedAt.ToString("O")); q.ExecuteNonQuery(); }

    private static decimal PreviewFifoCost(SqliteConnection c, SqliteTransaction tx, Guid productId, decimal quantity, decimal fallback)
    {
        using var q=c.CreateCommand(); q.Transaction=tx; q.CommandText="SELECT QuantityRemaining,UnitCost FROM CostLayers WHERE ProductId=$p AND QuantityRemaining>0 ORDER BY Id"; q.Parameters.AddWithValue("$p",productId.ToString()); using var r=q.ExecuteReader();
        decimal left=quantity,total=0; while(left>0 && r.Read()){var available=(decimal)r.GetDouble(0);var cost=(decimal)r.GetDouble(1);var take=Math.Min(left,available);total+=take*cost;left-=take;} return quantity<=0?fallback:(left>0?((total+left*fallback)/quantity):(total/quantity));
    }

    private static void ConsumeFifoLayers(SqliteConnection c, SqliteTransaction tx, Guid productId, decimal quantity)
    {
        decimal left=quantity; while(left>0)
        {
            long id=0; decimal available=0; using(var q=c.CreateCommand()){q.Transaction=tx;q.CommandText="SELECT Id,QuantityRemaining FROM CostLayers WHERE ProductId=$p AND QuantityRemaining>0 ORDER BY Id LIMIT 1";q.Parameters.AddWithValue("$p",productId.ToString());using var r=q.ExecuteReader();if(!r.Read())break;id=r.GetInt64(0);available=(decimal)r.GetDouble(1);}
            var take=Math.Min(left,available); using var u=c.CreateCommand();u.Transaction=tx;u.CommandText="UPDATE CostLayers SET QuantityRemaining=QuantityRemaining-$q WHERE Id=$id";u.Parameters.AddWithValue("$q",(double)take);u.Parameters.AddWithValue("$id",id);u.ExecuteNonQuery();left-=take;
        }
    }

    public IReadOnlyList<(string Product,string Reference,string Number,decimal Quantity,decimal Remaining,decimal UnitCost,string Currency,DateTime Date)> CostLayersReport()
    {
        using var c=Open();using var q=c.CreateCommand();q.CommandText="SELECT p.Name,COALESCE(l.ReferenceId,''),COALESCE(l.ReferenceNumber,''),l.QuantityIn,l.QuantityRemaining,l.UnitCost,l.Currency,l.CreatedAt FROM CostLayers l LEFT JOIN Products p ON p.Id=l.ProductId ORDER BY l.Id DESC";using var r=q.ExecuteReader();var list=new List<(string,string,string,decimal,decimal,decimal,string,DateTime)>();while(r.Read())list.Add((r.IsDBNull(0)?"":r.GetString(0),r.GetString(1),r.GetString(2),(decimal)r.GetDouble(3),(decimal)r.GetDouble(4),(decimal)r.GetDouble(5),r.GetString(6),DateTime.Parse(r.GetString(7))));return list;
    }

    private static void SeedPhase6Data(SqliteConnection c)
    {
        // The login migration creates the canonical admin row. Only seed a legacy
        // placeholder when the database genuinely has no administrator account.
        using var q = c.CreateCommand();
        q.CommandText = @"INSERT OR IGNORE INTO Users(Id,Name,Role,IsActive,CreatedAt)
SELECT 'admin','المدير','ADMIN',1,$date
WHERE NOT EXISTS (SELECT 1 FROM Users WHERE lower(Name)='admin' OR Name='المدير')";
        q.Parameters.AddWithValue("$date", DateTime.Now.ToString("O"));
        q.ExecuteNonQuery();
    }

    private void Log(string action,string entityType,string? entityId,string description,string performedBy="المدير")
    {
        using var c=Open(); using var q=c.CreateCommand();
        q.CommandText="INSERT INTO AuditLogs(Id,Action,EntityType,EntityId,Description,PerformedBy,Timestamp) VALUES($id,$a,$t,$e,$d,$p,$ts)";
        q.Parameters.AddWithValue("$id",Guid.NewGuid().ToString()); q.Parameters.AddWithValue("$a",action); q.Parameters.AddWithValue("$t",entityType); q.Parameters.AddWithValue("$e",entityId??""); q.Parameters.AddWithValue("$d",description); q.Parameters.AddWithValue("$p",performedBy); q.Parameters.AddWithValue("$ts",DateTime.Now.ToString("O")); q.ExecuteNonQuery();
    }

    public IReadOnlyList<(string Id,string Name,string Role,bool Active)> Users()
    {
        using var c=Open(); using var q=c.CreateCommand(); q.CommandText="SELECT Id,Name,Role,IsActive FROM Users ORDER BY Name"; using var r=q.ExecuteReader();
        var list=new List<(string,string,string,bool)>(); while(r.Read()) list.Add((r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt32(3)!=0)); return list;
    }

    public void SaveUser(string name,string role,string? pin,bool active=true)
    {
        if(string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("اسم المستخدم مطلوب.");
        if(role is not ("ADMIN" or "SUPERVISOR" or "CASHIER")) throw new InvalidOperationException("الصلاحية غير صحيحة.");
        var id=Guid.NewGuid().ToString();
        using var c=Open(); using var q=c.CreateCommand();
        q.CommandText="INSERT INTO Users(Id,Name,Role,PinHash,IsActive,CreatedAt) VALUES($id,$n,$r,$pin,$a,$d) ON CONFLICT(Name) DO UPDATE SET Role=$r,PinHash=$pin,IsActive=$a";
        q.Parameters.AddWithValue("$id",id); q.Parameters.AddWithValue("$n",name.Trim()); q.Parameters.AddWithValue("$r",role); q.Parameters.AddWithValue("$pin",string.IsNullOrWhiteSpace(pin)?"":HashPin(pin)); q.Parameters.AddWithValue("$a",active?1:0); q.Parameters.AddWithValue("$d",DateTime.Now.ToString("O")); q.ExecuteNonQuery();
        Log("USER_SAVE","USER",id,$"حفظ المستخدم {name}");
    }

    public void SetUserActive(string id,bool active)
    {
        using var c=Open(); using var q=c.CreateCommand(); q.CommandText="UPDATE Users SET IsActive=$a WHERE Id=$id"; q.Parameters.AddWithValue("$a",active?1:0); q.Parameters.AddWithValue("$id",id); q.ExecuteNonQuery(); Log("USER_STATUS","USER",id,active?"تفعيل المستخدم":"تعطيل المستخدم");
    }

    public IReadOnlyList<(DateTime Date,string Action,string Entity,string Description,string User)> AuditLog(int limit=200)
    {
        using var c=Open(); using var q=c.CreateCommand(); q.CommandText="SELECT Timestamp,Action,EntityType,Description,PerformedBy FROM AuditLogs ORDER BY Timestamp DESC LIMIT $n"; q.Parameters.AddWithValue("$n",limit); using var r=q.ExecuteReader();
        var list=new List<(DateTime,string,string,string,string)>(); while(r.Read()) list.Add((DateTime.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4))); return list;
    }

    public string CreateInventoryAudit(IEnumerable<(Guid ProductId,decimal Counted,string Reason)> lines,string notes="")
    {
        var items=lines.ToList(); if(items.Count==0) throw new InvalidOperationException("يجب اختيار صنف واحد على الأقل.");
        using var c=Open(); using var tx=c.BeginTransaction();
        var auditId=Guid.NewGuid().ToString(); var number=$"JRD-{DateTime.Now:yyyyMMdd-HHmmss}";
        void Exec(string sql,Action<SqliteCommand> fill){using var q=c.CreateCommand();q.Transaction=tx;q.CommandText=sql;fill(q);q.ExecuteNonQuery();}
        Exec("INSERT INTO InventoryAudits(Id,Date,Number,Status,Notes) VALUES($id,$d,$n,'POSTED',$x)",q=>{q.Parameters.AddWithValue("$id",auditId);q.Parameters.AddWithValue("$d",DateTime.Now.ToString("O"));q.Parameters.AddWithValue("$n",number);q.Parameters.AddWithValue("$x",notes);});
        foreach(var item in items)
        {
            var p=Products.FirstOrDefault(x=>x.Id==item.ProductId) ?? throw new InvalidOperationException("أحد الأصناف غير موجود.");
            var diff=item.Counted-p.Quantity; var value=diff*p.PurchasePrice;
            Exec("INSERT INTO InventoryAuditLines(AuditId,ProductId,ProductName,SystemQuantity,CountedQuantity,Difference,UnitCost,ValueDifference,Reason) VALUES($a,$p,$n,$s,$c,$d,$u,$v,$r)",q=>{q.Parameters.AddWithValue("$a",auditId);q.Parameters.AddWithValue("$p",p.Id.ToString());q.Parameters.AddWithValue("$n",p.Name);q.Parameters.AddWithValue("$s",(double)p.Quantity);q.Parameters.AddWithValue("$c",(double)item.Counted);q.Parameters.AddWithValue("$d",(double)diff);q.Parameters.AddWithValue("$u",(double)p.PurchasePrice);q.Parameters.AddWithValue("$v",(double)value);q.Parameters.AddWithValue("$r",item.Reason??"");});
            Exec("UPDATE Products SET Quantity=$q WHERE Id=$id",q=>{q.Parameters.AddWithValue("$q",(double)item.Counted);q.Parameters.AddWithValue("$id",p.Id.ToString());});
            Exec("INSERT INTO StockMovements(Id,ProductId,ProductName,Type,QuantityChange,QuantityBefore,QuantityAfter,UnitCost,UnitPrice,ReferenceId,Date,Notes) VALUES($id,$p,$n,'AUDIT',$d,$b,$a,$c,$u,$r,$dt,$x)",q=>{q.Parameters.AddWithValue("$id",Guid.NewGuid().ToString());q.Parameters.AddWithValue("$p",p.Id.ToString());q.Parameters.AddWithValue("$n",p.Name);q.Parameters.AddWithValue("$d",(double)diff);q.Parameters.AddWithValue("$b",(double)p.Quantity);q.Parameters.AddWithValue("$a",(double)item.Counted);q.Parameters.AddWithValue("$c",(double)p.PurchasePrice);q.Parameters.AddWithValue("$u",(double)p.SalePrice);q.Parameters.AddWithValue("$r",auditId);q.Parameters.AddWithValue("$dt",DateTime.Now.ToString("O"));q.Parameters.AddWithValue("$x",item.Reason??"تسوية جرد");});
        }
        tx.Commit(); LoadAll(); Log("INVENTORY_AUDIT","INVENTORY",auditId,$"اعتماد جرد {number} بعدد {items.Count} أصناف"); return number;
    }

    public void TransferCash(string fromFund,string toFund,decimal amount,decimal rate,string notes="")
    {
        if(fromFund==toFund) throw new InvalidOperationException("يجب اختيار صندوقين مختلفين."); if(amount<=0||rate<=0) throw new InvalidOperationException("المبلغ وسعر الصرف يجب أن يكونا أكبر من صفر.");
        var funds=CashFunds().ToDictionary(x=>x.Id); if(!funds.ContainsKey(fromFund)||!funds.ContainsKey(toFund)) throw new InvalidOperationException("الصندوق غير موجود.");
        if(funds[fromFund].Balance<amount) throw new InvalidOperationException("الرصيد غير كافٍ في الصندوق المصدر.");
        var target=amount*rate; var id=Guid.NewGuid().ToString();
        using var c=Open(); using var tx=c.BeginTransaction();
        void Exec(string sql,Action<SqliteCommand> fill){using var q=c.CreateCommand();q.Transaction=tx;q.CommandText=sql;fill(q);q.ExecuteNonQuery();}
        Exec("INSERT INTO CashTransfers(Id,Date,FromFundId,ToFundId,Amount,FromCurrency,ToCurrency,Rate,TargetAmount,Notes) VALUES($id,$d,$f,$t,$a,$fc,$tc,$r,$ta,$n)",q=>{q.Parameters.AddWithValue("$id",id);q.Parameters.AddWithValue("$d",DateTime.Now.ToString("O"));q.Parameters.AddWithValue("$f",fromFund);q.Parameters.AddWithValue("$t",toFund);q.Parameters.AddWithValue("$a",(double)amount);q.Parameters.AddWithValue("$fc",funds[fromFund].Currency);q.Parameters.AddWithValue("$tc",funds[toFund].Currency);q.Parameters.AddWithValue("$r",(double)rate);q.Parameters.AddWithValue("$ta",(double)target);q.Parameters.AddWithValue("$n",notes);});
        Exec("INSERT INTO CashTransactions(Id,Date,FundId,Type,Amount,Currency,Description,ReferenceId) VALUES($i,$d,$f,'TRANSFER_OUT',$a,$c,$n,$r)",q=>{q.Parameters.AddWithValue("$i",Guid.NewGuid().ToString());q.Parameters.AddWithValue("$d",DateTime.Now.ToString("O"));q.Parameters.AddWithValue("$f",fromFund);q.Parameters.AddWithValue("$a",(double)amount);q.Parameters.AddWithValue("$c",funds[fromFund].Currency);q.Parameters.AddWithValue("$n","تحويل بين الصناديق");q.Parameters.AddWithValue("$r",id);});
        Exec("INSERT INTO CashTransactions(Id,Date,FundId,Type,Amount,Currency,Description,ReferenceId) VALUES($i,$d,$f,'TRANSFER_IN',$a,$c,$n,$r)",q=>{q.Parameters.AddWithValue("$i",Guid.NewGuid().ToString());q.Parameters.AddWithValue("$d",DateTime.Now.ToString("O"));q.Parameters.AddWithValue("$f",toFund);q.Parameters.AddWithValue("$a",(double)target);q.Parameters.AddWithValue("$c",funds[toFund].Currency);q.Parameters.AddWithValue("$n","استلام تحويل من صندوق آخر");q.Parameters.AddWithValue("$r",id);});
        var eid=Guid.NewGuid().ToString(); var desc=string.IsNullOrWhiteSpace(notes)?"تحويل بين الصناديق":"تحويل بين الصناديق - "+notes;
        Exec("INSERT INTO JournalEntries VALUES($id,$d,$x,$r)",q=>{q.Parameters.AddWithValue("$id",eid);q.Parameters.AddWithValue("$d",DateTime.Now.ToString("O"));q.Parameters.AddWithValue("$x",desc);q.Parameters.AddWithValue("$r",id);});
        Exec("INSERT INTO JournalLines(EntryId,Account,Debit,Credit) VALUES($e,$to,$d,0),($e,$from,0,$c)",q=>{q.Parameters.AddWithValue("$e",eid);q.Parameters.AddWithValue("$to",CashedAccount(funds[toFund].Currency));q.Parameters.AddWithValue("$from",CashedAccount(funds[fromFund].Currency));q.Parameters.AddWithValue("$d",(double)amount);q.Parameters.AddWithValue("$c",(double)amount);});
        tx.Commit(); LoadAll(); Log("CASH_TRANSFER","CASH",id,$"تحويل {amount:N2} {funds[fromFund].Currency} إلى {target:N2} {funds[toFund].Currency}");
    }

    public IReadOnlyList<(DateTime Date,string Number,string Status,string Notes,decimal DifferenceValue)> InventoryAudits()
    {
        using var c=Open(); using var q=c.CreateCommand(); q.CommandText="SELECT a.Date,a.Number,a.Status,a.Notes,COALESCE(SUM(l.ValueDifference),0) FROM InventoryAudits a LEFT JOIN InventoryAuditLines l ON l.AuditId=a.Id GROUP BY a.Id ORDER BY a.Date DESC"; using var r=q.ExecuteReader();
        var list=new List<(DateTime,string,string,string,decimal)>(); while(r.Read()) list.Add((DateTime.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),r.GetString(3),(decimal)r.GetDouble(4))); return list;
    }

    public IReadOnlyList<(string Product,string Type,decimal Change,decimal Before,decimal After,decimal Cost,DateTime Date,string Reference)> StockMovements(string? productId=null)
    {
        using var c=Open(); using var q=c.CreateCommand(); q.CommandText="SELECT ProductName,Type,QuantityChange,QuantityBefore,QuantityAfter,UnitCost,Date,ReferenceId FROM StockMovements WHERE ($p='' OR ProductId=$p) ORDER BY Date DESC"; q.Parameters.AddWithValue("$p",productId??""); using var r=q.ExecuteReader();
        var list=new List<(string,string,decimal,decimal,decimal,decimal,DateTime,string)>(); while(r.Read()) list.Add((r.GetString(0),r.GetString(1),(decimal)r.GetDouble(2),(decimal)r.GetDouble(3),(decimal)r.GetDouble(4),(decimal)r.GetDouble(5),DateTime.Parse(r.GetString(6)),r.GetString(7))); return list;
    }

    private static string HashPin(string pin) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pin)));

}
