$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root 'desktop\mizan\MizanDesktop.csproj'
$AppXaml = Join-Path $Root 'desktop\mizan\App.xaml'
$MainXaml = Join-Path $Root 'desktop\mizan\MainWindow.xaml'
$LoginXaml = Join-Path $Root 'desktop\mizan\LoginWindow.xaml'

if (-not (Select-String -Path $Project -Pattern '<TargetFramework>net10\.0-windows</TargetFramework>' -Quiet)) {
  throw 'The WPF target framework must be net10.0-windows.'
}

$main = Get-Content $MainXaml -Raw
if ($main -notmatch 'WindowState="Maximized"') { throw 'Main window should start maximized.' }
if ($main -notmatch 'VerticalScrollBarVisibility="Auto"') { throw 'Main window navigation/content needs vertical scroll support.' }
if ($main -notmatch 'NavButton') { throw 'Navigation button style is missing.' }

$app = Get-Content $AppXaml -Raw
foreach ($resource in @('Primary','Text','Muted','ControlBorder','NavButton','DataGridColumnHeader')) {
  if ($app -notmatch [regex]::Escape($resource)) { throw "Shared accessible UI resource is missing: $resource" }
}

$login = Get-Content $LoginXaml -Raw
if ($login -notmatch 'RecoverAdmin_Click' -or $login -notmatch 'استعادة دخول المدير') {
  throw 'Login recovery action is missing from the login window.'
}

$eventNames = @('Click','TextChanged','SelectionChanged','MouseDoubleClick','KeyDown','Loaded','Checked','Unchecked','LostFocus','PreviewKeyDown','SelectedDateChanged','MouseUp','MouseDown')
$xamls = Get-ChildItem (Join-Path $Root 'desktop\mizan') -Recurse -Filter '*.xaml'
$checked = 0
$eventCount = 0
foreach ($xaml in $xamls) {
  try {
    $xml = New-Object System.Xml.XmlDocument
    $xml.Load($xaml.FullName)
  } catch {
    throw "Invalid XAML XML: $($xaml.FullName): $($_.Exception.Message)"
  }
  $codeBehind = [regex]::Replace($xaml.FullName, '\.xaml$', '.xaml.cs')
  $code = if (Test-Path $codeBehind) { Get-Content $codeBehind -Raw } else { '' }
  foreach ($node in $xml.SelectNodes('//*')) {
    foreach ($attribute in $node.Attributes) {
      if ($attribute.LocalName -in $eventNames -and -not [string]::IsNullOrWhiteSpace($attribute.Value)) {
        $handler = $attribute.Value.Trim()
        $pattern = '\b' + [regex]::Escape($handler) + '\s*\('
        if ($code -notmatch $pattern) {
          throw "Missing XAML event handler '$handler' in $codeBehind (file: $($xaml.Name))."
        }
        $eventCount++
      }
    }
  }
  $checked++
}
Write-Host "UI validation passed: $checked XAML files parsed, $eventCount event handlers found, .NET 10 target, maximized shell, scrollbars, shared contrast styles, and login recovery." -ForegroundColor Green
