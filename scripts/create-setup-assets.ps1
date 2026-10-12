param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

function Draw-Mark($graphics,[float]$x,[float]$y,[float]$size) {
 $background=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(24,24,24))
 $foreground=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(245,245,245))
 $edge=[Drawing.Pen]::new([Drawing.Color]::FromArgb(58,58,58),1)
 $font=[Drawing.Font]::new('Segoe UI',[float]($size*0.52),[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
 $format=[Drawing.StringFormat]::new()
 $format.Alignment=[Drawing.StringAlignment]::Center
 $format.LineAlignment=[Drawing.StringAlignment]::Center
 $path=[Drawing.Drawing2D.GraphicsPath]::new()
 $radius=[float]($size*0.23)
 $path.AddArc($x,$y,$radius,$radius,180,90)
 $path.AddArc($x+$size-$radius,$y,$radius,$radius,270,90)
 $path.AddArc($x+$size-$radius,$y+$size-$radius,$radius,$radius,0,90)
 $path.AddArc($x,$y+$size-$radius,$radius,$radius,90,90)
 $path.CloseFigure()
 try {
  $graphics.FillPath($background,$path)
  $graphics.DrawPath($edge,$path)
  $graphics.DrawString('N',$font,$foreground,[Drawing.RectangleF]::new($x,$y-1,$size,$size),$format)
 } finally { $path.Dispose(); $format.Dispose(); $font.Dispose(); $edge.Dispose(); $foreground.Dispose(); $background.Dispose() }
}

$dialog=[Drawing.Bitmap]::new(493,312)
$graphics=[Drawing.Graphics]::FromImage($dialog)
$white=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(245,245,245))
$muted=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(140,140,140))
$wordmark=[Drawing.Font]::new('Segoe UI',14,[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
$caption=[Drawing.Font]::new('Segoe UI',10,[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel)
$line=[Drawing.Pen]::new([Drawing.Color]::FromArgb(42,42,42),1)
try {
 $graphics.Clear([Drawing.Color]::White)
 $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
 $graphics.TextRenderingHint=[Drawing.Text.TextRenderingHint]::AntiAliasGridFit
 $graphics.FillRectangle([Drawing.Brushes]::Black,0,0,164,312)
 Draw-Mark $graphics 30 43 54
 $graphics.DrawString('NULLTV',$wordmark,$white,29,116)
 $graphics.DrawLine($line,30,152,134,152)
 $graphics.DrawString("YOUR SCREEN.`nYOUR SOURCES.",$caption,$muted,29,170)
 $graphics.DrawString('WINDOWS 11 ARM64',$caption,$muted,29,275)
 $dialog.Save((Join-Path $OutputDirectory 'setup-dialog.bmp'),[Drawing.Imaging.ImageFormat]::Bmp)
} finally { $line.Dispose(); $caption.Dispose(); $wordmark.Dispose(); $muted.Dispose(); $white.Dispose(); $graphics.Dispose(); $dialog.Dispose() }

$banner=[Drawing.Bitmap]::new(493,58)
$graphics=[Drawing.Graphics]::FromImage($banner)
try {
 $graphics.Clear([Drawing.Color]::White)
 $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
 $graphics.TextRenderingHint=[Drawing.Text.TextRenderingHint]::AntiAliasGridFit
 Draw-Mark $graphics 443 11 36
 $banner.Save((Join-Path $OutputDirectory 'setup-banner.bmp'),[Drawing.Imaging.ImageFormat]::Bmp)
} finally { $graphics.Dispose(); $banner.Dispose() }
