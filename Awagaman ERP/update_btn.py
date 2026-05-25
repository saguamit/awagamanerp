with open('MainWindow.xaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('<Button Grid.Column="1" Content="+ Create LR" Style="{StaticResource GreenButtonStyle}" />', '<Button Grid.Column="1" Content="+ Create LR" Style="{StaticResource GreenButtonStyle}" Click="OpenLRForm_Click" />')

with open('MainWindow.xaml', 'w', encoding='utf-8') as f:
    f.write(text)

print('Updated MainWindow.xaml')
