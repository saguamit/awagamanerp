import sys

with open('MainWindow.xaml', 'rb') as f:
    raw = f.read()

if raw.startswith(b'\xef\xbb\xbf'):
    raw = raw[3:]

text = raw.decode('utf-8')

if not text.startswith('<?xml'):
    text = '<?xml version="1.0" encoding="utf-8"?>\n' + text

with open('MainWindow.xaml', 'w', encoding='utf-8') as f:
    f.write(text)

print('Fixed XML declaration')
