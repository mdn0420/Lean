# -*- coding: utf-8 -*-

import csv
import datetime
import pytz

format_results = []
with open('/Users/mnguyen/gitrepos/Lean/Data/trades/vanguer_trades.csv') as csv_file:
    csv_reader = csv.reader(csv_file, delimiter=',')
    line_count = 0
    timezone = pytz.timezone('America/New_York')
    last_date_str = ''
    last_symbol = ''
    for row in csv_reader:
        date_str = row[0]
        date_obj = datetime.datetime.strptime(date_str, '%d/%m/%Y')
        date_obj = timezone.localize(date_obj)
        date_obj = date_obj.replace(hour=17)
        symbol = row[1]
        position = row[2]
        
        line_count += 1
        
        # remove duplicates
        if last_date_str == date_str and last_symbol == symbol:
            continue
        
        last_date_str = date_str
        last_symbol = symbol
        
        format_results.append([date_obj,symbol,position])
        print(f'{date_obj} {symbol}')
    print(f'Processed {line_count} lines. {len(format_results)} results.')
    
with open('/Users/mnguyen/gitrepos/Lean/Data/trades/vanguer_trades2.csv', mode='w') as output_file:
    output_writer = csv.writer(output_file, delimiter=',', quotechar='"', quoting=csv.QUOTE_MINIMAL)
    for row in format_results:
        output_writer.writerow(row)