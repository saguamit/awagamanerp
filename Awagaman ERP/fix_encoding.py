import sys

with open('MainWindow.xaml', 'r', encoding='utf-8') as f:
    text = f.read()

replacements = {
    'ðŸ“Š': '📊',
    'ðŸšš': '🚚',
    'ðŸ›’': '🛒',
    'â†©': '↩',
    'â‰¡': '≡',
    'ðŸ“¥': '📥',
    'ðŸ“„': '📄',
    'âŠž': '⊞',
    'â†»': '↻',
    'âš™': '⚙',
    'âœŽ': '✎',
    'ðŸ—‘': '🗑',
    'â–¾': '▾',
    'â‚¹': '₹',
    'â°': '🚚'
}

for corrupted, correct in replacements.items():
    text = text.replace(corrupted, correct)

with open('MainWindow.xaml', 'w', encoding='utf-8-sig') as f:
    f.write(text)

print('Success')
