using System.Windows;
using System.Windows.Controls;
using MizanDesktop.Services;
namespace MizanDesktop.Views;
public sealed class UsersView:UserControl
{
 readonly AppRepository _repo; readonly DataGrid _grid=new(){AutoGenerateColumns=false,IsReadOnly=true}; readonly TextBox _name=new(); readonly PasswordBox _pin=new(); readonly ComboBox _role=new();
 public UsersView(AppRepository repo){_repo=repo;FlowDirection=FlowDirection.RightToLeft;var root=new DockPanel{Margin=new Thickness(20)};var title=new TextBlock{Text="المستخدمون والصلاحيات",FontSize=24,FontWeight=FontWeights.Bold};DockPanel.SetDock(title,Dock.Top);root.Children.Add(title);var form=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,15,0,10)};_name.Width=180;_pin.Width=120;_role.Width=140;_role.ItemsSource=new[]{"ADMIN","SUPERVISOR","CASHIER"};_role.SelectedIndex=0;form.Children.Add(LabelBox("الاسم",_name));form.Children.Add(LabelBox("PIN",_pin));form.Children.Add(LabelBox("الدور",_role));var add=new Button{Content="حفظ المستخدم",Padding=new Thickness(14),Margin=new Thickness(8,20,0,0)};add.Click+=Save;form.Children.Add(add);DockPanel.SetDock(form,Dock.Top);root.Children.Add(form);_grid.Columns.Add(new DataGridTextColumn{Header="الاسم",Binding=new System.Windows.Data.Binding("Name")});_grid.Columns.Add(new DataGridTextColumn{Header="الدور",Binding=new System.Windows.Data.Binding("Role")});_grid.Columns.Add(new DataGridCheckBoxColumn{Header="فعال",Binding=new System.Windows.Data.Binding("Active")});root.Children.Add(_grid);Content=root;Refresh();}
 FrameworkElement LabelBox(string label,Control c){var p=new StackPanel{Margin=new Thickness(0,0,8,0)};p.Children.Add(new TextBlock{Text=label});p.Children.Add(c);return p;}
 void Refresh(){_grid.ItemsSource=_repo.Users();}
 void Save(object? s,RoutedEventArgs e){try{_repo.SaveUser(_name.Text,(string)(_role.SelectedItem??"CASHIER"),_pin.Password);Refresh();_name.Clear();_pin.Clear();MessageBox.Show("تم حفظ المستخدم.");}catch(Exception ex){MessageBox.Show(ex.Message,"المستخدمون",MessageBoxButton.OK,MessageBoxImage.Error);}}
}
