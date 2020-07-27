# -*- coding: utf-8 -*-
import pandas as pd
import leantools as lt
import pytz as tz
import os

#pwd = os.path.abspath('')
script_dir = os.path.dirname(__file__) #<-- absolute dir the script is in
result_dir = os.path.join (script_dir, "../../results/parallax/backtest2")
algo_result_filepath = os.path.join(result_dir, "USDCAD-H4.json")
analysis_data_filepath = os.path.join(result_dir, "USDCAD-H4-analysis_data.json")

include_hours = True
    
# bar_data_df = get_bar_data_df(analysis_data_filepath)
# print(bar_data_df)

trade_setups_df = lt.get_trade_setups_df(analysis_data_filepath)
print(trade_setups_df.dtypes)
print(trade_setups_df)

for row in trade_setups_df.itertuples():
    dt = row[0].astimezone(tz.timezone('America/New_York'))
    hour = dt.hour if include_hours else "0"
    value = 0
    if row.plPips >= 0:
        value = 1
    else:
        value = -1
        
    line = f'd := t == timestamp({dt.year}, {dt.month}, {dt.day}, {hour}, 0, 0) ? {value} : d'
    print(line)