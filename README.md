# MacTrafficLights

Windows pencerelerinde kapatma, küçültme ve tam ekran tuşlarını sağ üst yerine macOS tarzı sol üst köşede gösteren hafif bir araç.

## Özellikler

- Aktif pencerenin sol üst köşesine otomatik yerleşir (`DWMWA_EXTENDED_FRAME_BOUNDS` ve `WinEventHook` ile).
- Başlık çubuğunun rengini algılayıp arkadaki pencere ikonunu ve yazıyı kapatan uyumlu şerit çeker.
- Standart Windows pencerelerinde sağdaki orijinal kontrol tuşlarını gizleyebilir (`WS_SYSMENU`).
- Sistem tepsisinden (System Tray) şerit genişliği ve köşe konumu ayarlanabilir.
- Uygulamadan çıkıldığında pencereleri eski haline döndürür.

## Derleme

```powershell
dotnet build -c Release
```

Tek dosya `.exe` çıktısı almak için:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```
