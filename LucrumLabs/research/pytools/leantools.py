# -*- coding: utf-8 -*-

# Grabs closed orders and generates Pine Script to draw on TradingView

import pandas as pd
import json
#print(json.dumps(trades_json, indent=4))


#print(trades_df)
    
def get_closed_trades_df(path):
    with open(path) as f:
        data = json.load(f)

    trades_json = data['TotalPerformance']['ClosedTrades']

    #print(json.dumps(trades_json, indent=4))
    return pd.json_normalize(trades_json)

def get_bar_data_df(path):
    with open(path) as f:
        data = json.load(f)
        
    bar_data = data['BarData']
    return pd.read_json(json.dumps(bar_data), convert_dates=['Time']).set_index('Time')

def get_trade_setups_df(path):
    with open(path) as f:
        data = json.load(f)
        
    bar_data = data['TradeSetups']
    return pd.read_json(json.dumps(bar_data), convert_dates=['BarTime']).set_index('BarTime')

