import sys

with open('MainWindow.xaml', 'r', encoding='utf-8') as f:
    text = f.read()

start_idx = text.find('<!-- Row 1: Summary Ribbon -->')
end_idx = text.find('<!-- LR Ledger View Overlay -->')

if start_idx == -1 or end_idx == -1:
    print('Failed to find markers')
    sys.exit(1)

inner_text = text[start_idx:end_idx]

inner_text = inner_text.replace('Border Grid.Row="1"', 'Border Grid.Row="0"')
inner_text = inner_text.replace('Border Grid.Row="2"', 'Border Grid.Row="1"')
inner_text = inner_text.replace('Grid Grid.Row="3"', 'Grid Grid.Row="2"')
inner_text = inner_text.replace('Border Grid.Row="4"', 'Border Grid.Row="3"')

new_inner = f"""<!-- Delivery Challan View -->
                    <Grid x:Name="DeliveryChallanView" Grid.Row="1" Grid.RowSpan="4">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="*" />
                        </Grid.RowDefinitions>
{inner_text}                    </Grid>
                    """

new_text = text[:start_idx] + new_inner + text[end_idx:]

with open('MainWindow.xaml', 'w', encoding='utf-8') as f:
    f.write(new_text)
print('Success')
