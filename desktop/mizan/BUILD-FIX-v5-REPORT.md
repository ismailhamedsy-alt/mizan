# Windows build-fix v5

## Errors from the supplied build log
The previous build stopped with 68 compiler errors. The corrected source addresses:
- WPF `TextBox` constructor misuse in CashTransferView.
- Missing System.IO names (`Path`, `File`, `Directory`, StreamWriter, exceptions) via GlobalUsings.
- Missing `ReportingService.ToXlsx` API.
- Missing `MirrorParityToOperational`.
- Missing `Query` helper.
- Incorrect `DataGrid.SetRow` calls.
- Incorrect InventoryReport tuple names.
- Incorrect `AccountBalance` 4-argument construction.
- Incorrect `ToDictionary` call in EnterpriseAccountingService.

## Runtime hardening added
- SQLite parity copy reads now use a separate read connection, avoiding nested-reader/write conflicts.
- Journal parity lines now resolve `account_id` against copied account IDs/codes/names before insertion.

## Build command
Run only:
`1-BUILD-MIZAN.cmd`

Expected EXE:
`desktop\mizan\dist\Mizan.exe`

The environment used to prepare this package is not Windows and cannot execute `dotnet build/publish`, so final compilation must still be performed on the user's Windows machine.
