using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;

namespace People.Converters;

public class ByteArrayToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (value is byte[] bytes && bytes.Length > 0)
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            stream.Position = 0;
            bitmap.SetSource(stream.AsRandomAccessStream());
            return bitmap;
        }
        
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, string language)
    {
        throw new NotImplementedException();
    }
}
