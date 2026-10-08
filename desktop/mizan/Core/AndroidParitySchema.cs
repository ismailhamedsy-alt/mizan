using Microsoft.Data.Sqlite;

namespace MizanDesktop.Core;

/// <summary>
/// Windows database foundation aligned with the Android application's relational schema.
/// The UI can be migrated module-by-module without losing the accounting/inventory model.
/// </summary>
public static class AndroidParitySchema
{
    public static void Ensure(SqliteConnection db)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
        PRAGMA foreign_keys = ON;
        PRAGMA journal_mode = WAL;

        CREATE TABLE IF NOT EXISTS tbl_kv (
            k TEXT PRIMARY KEY,
            v TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS tbl_products (
            id TEXT PRIMARY KEY,
            barcode TEXT,
            name TEXT NOT NULL,
            category TEXT,
            unit TEXT,
            cost_price REAL DEFAULT 0,
            sale_price REAL DEFAULT 0,
            cost_price_usd REAL DEFAULT 0,
            sale_price_usd REAL DEFAULT 0,
            wholesale_price_syp REAL DEFAULT 0,
            wholesale_price_usd REAL DEFAULT 0,
            special_price_syp REAL DEFAULT 0,
            special_price_usd REAL DEFAULT 0,
            last_cost_price_syp REAL DEFAULT 0,
            last_cost_price_usd REAL DEFAULT 0,
            previous_cost_price_syp REAL DEFAULT 0,
            previous_cost_price_usd REAL DEFAULT 0,
            quantity REAL DEFAULT 0,
            min_stock_alert REAL DEFAULT 0,
            expiry_date INTEGER DEFAULT 0,
            notes TEXT,
            created_at INTEGER DEFAULT 0,
            default_currency TEXT,
            is_archived INTEGER DEFAULT 0,
            raw_json TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_prod_barcode ON tbl_products(barcode);
        CREATE INDEX IF NOT EXISTS idx_prod_category ON tbl_products(category);
        CREATE INDEX IF NOT EXISTS idx_prod_name ON tbl_products(name);

        CREATE TABLE IF NOT EXISTS tbl_parties (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            phone TEXT,
            address TEXT,
            type TEXT NOT NULL,
            default_currency TEXT,
            credit_limit REAL DEFAULT 0,
            price_tier TEXT,
            notes TEXT,
            created_at INTEGER DEFAULT 0,
            balance REAL DEFAULT 0,
            currency TEXT,
            raw_json TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_party_name ON tbl_parties(name);
        CREATE INDEX IF NOT EXISTS idx_party_type ON tbl_parties(type);
        CREATE INDEX IF NOT EXISTS idx_party_phone ON tbl_parties(phone);

        CREATE TABLE IF NOT EXISTS tbl_invoices (
            id TEXT PRIMARY KEY,
            number TEXT NOT NULL,
            type TEXT NOT NULL,
            party_id TEXT,
            party_name TEXT,
            net_amount REAL DEFAULT 0,
            paid_amount REAL DEFAULT 0,
            remaining_amount REAL DEFAULT 0,
            discount REAL DEFAULT 0,
            fees_amount REAL DEFAULT 0,
            fees_note TEXT,
            currency TEXT NOT NULL,
            date INTEGER NOT NULL,
            is_cancelled INTEGER DEFAULT 0,
            status TEXT,
            notes TEXT,
            cashier_name TEXT,
            payment_method TEXT,
            fund_id TEXT,
            raw_json TEXT,
            FOREIGN KEY (party_id) REFERENCES tbl_parties(id) ON DELETE SET NULL
        );
        CREATE INDEX IF NOT EXISTS idx_inv_date ON tbl_invoices(date DESC);
        CREATE INDEX IF NOT EXISTS idx_inv_number ON tbl_invoices(number);
        CREATE INDEX IF NOT EXISTS idx_inv_party ON tbl_invoices(party_id);
        CREATE INDEX IF NOT EXISTS idx_inv_type ON tbl_invoices(type);
        CREATE INDEX IF NOT EXISTS idx_inv_party_date ON tbl_invoices(party_id, date DESC);

        CREATE TABLE IF NOT EXISTS tbl_invoice_items (
            id TEXT PRIMARY KEY,
            invoice_id TEXT NOT NULL,
            product_id TEXT,
            product_name TEXT NOT NULL,
            quantity REAL NOT NULL,
            unit_price REAL NOT NULL,
            unit_cost REAL DEFAULT 0,
            unit TEXT,
            total_price REAL NOT NULL,
            raw_json TEXT,
            FOREIGN KEY (invoice_id) REFERENCES tbl_invoices(id) ON DELETE CASCADE,
            FOREIGN KEY (product_id) REFERENCES tbl_products(id) ON DELETE SET NULL
        );
        CREATE INDEX IF NOT EXISTS idx_item_invoice ON tbl_invoice_items(invoice_id);
        CREATE INDEX IF NOT EXISTS idx_item_product ON tbl_invoice_items(product_id);

        CREATE TABLE IF NOT EXISTS tbl_cash_transactions (
            id TEXT PRIMARY KEY,
            tx_number TEXT NOT NULL,
            fund_id TEXT NOT NULL,
            type TEXT NOT NULL,
            amount REAL NOT NULL,
            currency TEXT NOT NULL,
            description TEXT,
            category TEXT,
            party_id TEXT,
            party_name TEXT,
            invoice_id TEXT,
            cashier_name TEXT,
            payment_method TEXT,
            is_cancelled INTEGER DEFAULT 0,
            cancelled_at INTEGER DEFAULT 0,
            cancel_reason TEXT,
            target_currency TEXT,
            exchange_rate REAL DEFAULT 1,
            target_amount REAL DEFAULT 0,
            date INTEGER NOT NULL,
            raw_json TEXT,
            FOREIGN KEY (party_id) REFERENCES tbl_parties(id) ON DELETE SET NULL,
            FOREIGN KEY (invoice_id) REFERENCES tbl_invoices(id) ON DELETE SET NULL
        );
        CREATE INDEX IF NOT EXISTS idx_cash_date ON tbl_cash_transactions(date DESC);
        CREATE INDEX IF NOT EXISTS idx_cash_fund ON tbl_cash_transactions(fund_id);
        CREATE INDEX IF NOT EXISTS idx_cash_party ON tbl_cash_transactions(party_id);
        CREATE INDEX IF NOT EXISTS idx_cash_inv ON tbl_cash_transactions(invoice_id);

        CREATE TABLE IF NOT EXISTS tbl_stock_movements (
            id TEXT PRIMARY KEY,
            product_id TEXT NOT NULL,
            product_name TEXT NOT NULL,
            type TEXT NOT NULL,
            quantity_change REAL NOT NULL,
            quantity_before REAL NOT NULL,
            quantity_after REAL NOT NULL,
            unit_cost REAL,
            unit_price REAL,
            currency TEXT,
            reference_type TEXT,
            reference_id TEXT,
            party_id TEXT,
            date INTEGER NOT NULL,
            notes TEXT,
            raw_json TEXT,
            FOREIGN KEY (product_id) REFERENCES tbl_products(id) ON DELETE SET NULL,
            FOREIGN KEY (party_id) REFERENCES tbl_parties(id) ON DELETE SET NULL
        );
        CREATE INDEX IF NOT EXISTS idx_mov_prod ON tbl_stock_movements(product_id);
        CREATE INDEX IF NOT EXISTS idx_mov_date ON tbl_stock_movements(date DESC);
        CREATE INDEX IF NOT EXISTS idx_stock_ref ON tbl_stock_movements(reference_id);
        CREATE INDEX IF NOT EXISTS idx_stock_party ON tbl_stock_movements(party_id);

        CREATE TABLE IF NOT EXISTS tbl_payments (
            id TEXT PRIMARY KEY,
            payment_number INTEGER,
            invoice_id TEXT,
            party_id TEXT,
            party_name TEXT,
            fund_id TEXT,
            amount REAL NOT NULL,
            currency TEXT NOT NULL,
            type TEXT NOT NULL,
            payment_method TEXT,
            date INTEGER NOT NULL,
            is_cancelled INTEGER DEFAULT 0,
            raw_json TEXT,
            FOREIGN KEY (party_id) REFERENCES tbl_parties(id) ON DELETE SET NULL,
            FOREIGN KEY (invoice_id) REFERENCES tbl_invoices(id) ON DELETE SET NULL
        );
        CREATE INDEX IF NOT EXISTS idx_pay_invoice ON tbl_payments(invoice_id);
        CREATE INDEX IF NOT EXISTS idx_pay_party ON tbl_payments(party_id);
        CREATE INDEX IF NOT EXISTS idx_pay_date ON tbl_payments(date DESC);

        CREATE TABLE IF NOT EXISTS tbl_accounts (
            id TEXT PRIMARY KEY,
            code TEXT NOT NULL UNIQUE,
            name_ar TEXT NOT NULL,
            type TEXT NOT NULL,
            currency TEXT,
            raw_json TEXT
        );

        CREATE TABLE IF NOT EXISTS tbl_journal_entries (
            id TEXT PRIMARY KEY,
            entry_number INTEGER NOT NULL,
            date INTEGER NOT NULL,
            description TEXT NOT NULL,
            reference_type TEXT,
            reference_id TEXT,
            is_cancelled INTEGER DEFAULT 0,
            raw_json TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_entry_date ON tbl_journal_entries(date DESC);
        CREATE INDEX IF NOT EXISTS idx_entry_number ON tbl_journal_entries(entry_number);
        CREATE INDEX IF NOT EXISTS idx_journal_ref ON tbl_journal_entries(reference_id);

        CREATE TABLE IF NOT EXISTS tbl_journal_lines (
            id TEXT PRIMARY KEY,
            entry_id TEXT NOT NULL,
            account_id TEXT NOT NULL,
            account_code TEXT,
            account_name TEXT,
            debit REAL DEFAULT 0,
            credit REAL DEFAULT 0,
            currency TEXT,
            raw_json TEXT,
            FOREIGN KEY (entry_id) REFERENCES tbl_journal_entries(id) ON DELETE CASCADE,
            FOREIGN KEY (account_id) REFERENCES tbl_accounts(id) ON DELETE RESTRICT
        );
        CREATE INDEX IF NOT EXISTS idx_line_entry ON tbl_journal_lines(entry_id);
        CREATE INDEX IF NOT EXISTS idx_line_account ON tbl_journal_lines(account_id);

        CREATE TABLE IF NOT EXISTS tbl_audit_logs (
            id TEXT PRIMARY KEY,
            action TEXT NOT NULL,
            entity_type TEXT NOT NULL,
            entity_id TEXT,
            description TEXT,
            performed_by TEXT,
            timestamp INTEGER NOT NULL,
            raw_json TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_audit_time ON tbl_audit_logs(timestamp DESC);

        CREATE TABLE IF NOT EXISTS tbl_users (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            role TEXT NOT NULL,
            pin TEXT,
            pin_hash TEXT,
            pin_salt TEXT,
            raw_json TEXT
        );

        CREATE TABLE IF NOT EXISTS tbl_license (
            id TEXT PRIMARY KEY,
            license_id TEXT,
            device_id TEXT,
            plan TEXT,
            status TEXT,
            digital_signature TEXT,
            raw_json TEXT
        );
        """;
        cmd.ExecuteNonQuery();
    }
}
