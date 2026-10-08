using System.Windows;
namespace MizanDesktop;
public partial class App:Application{public static (string Id,string Name,string Role)? CurrentUser{get;private set;}public static void SetUser((string Id,string Name,string Role) u)=>CurrentUser=u;public static void Logout(){CurrentUser=null;Current.Shutdown();}}
