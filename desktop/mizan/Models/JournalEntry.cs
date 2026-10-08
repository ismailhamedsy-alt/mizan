namespace MizanDesktop.Models;
public sealed class JournalEntry { public Guid Id {get;set;}=Guid.NewGuid(); public DateTime Date {get;set;}=DateTime.Now; public string Description {get;set;}=""; public string Reference {get;set;}=""; public List<JournalLine> Lines {get;set;}=[]; }
public sealed class JournalLine { public string Account {get;set;}=""; public decimal Debit {get;set;} public decimal Credit {get;set;} }
