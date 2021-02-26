# -*- coding: utf-8 -*-

# Grabs closed orders and generates Pine Script to draw on TradingView

import pandas as pd
import json

# Get summary statistics
def get_trade_statistics(path):
    with open(path) as f:
        data = json.load(f)
    stats = {}
    stats['TradeStats'] = data['TotalPerformance']['TradeStatistics']
    stats['PortfolioStats'] = data['TotalPerformance']['PortfolioStatistics']
    return pd.json_normalize(stats)
    
# Get data on trades that were actually open and closed
def get_closed_trades_df(path):
    with open(path) as f:
        data = json.load(f)

    trades_json = data['TotalPerformance']['ClosedTrades']

    #print(json.dumps(trades_json, indent=4))
    #df = pd.json_normalize(trades_json)
    #df['EntryTime'] = pd.to_datetime(df['EntryTime'])
    return pd.json_normalize(trades_json)

def get_bar_data_df(path):
    with open(path) as f:
        data = json.load(f)
    bar_data = data['BarData']
    return pd.read_json(json.dumps(bar_data), convert_dates=['Time']).set_index(['Symbol','Time'])

# Get the trade setup data generated from algorithm
def get_trade_setups_df(path):
    with open(path) as f:
        data = json.load(f)
        
    bar_data = data['TradeSetups']
    return pd.read_json(json.dumps(bar_data), convert_dates=['BarTime']).set_index('BarTime')

def get_bar_ratios(o, h, l, c):
    length = h - l;
    body_top = max(o, c)
    body_bottom = min(o, c)
    
    if length < 0.0000001:
        ratios = {
            "top": 0,
            "body": 0,
            "bottom": 0
        }
    else:
        ratios = {
            "top": (h - body_top) / length,
            "body": (body_top - body_bottom) / length,
            "bottom": (body_bottom - l) / length
        }
    return ratios

def getPipSize(ticker):
    if ticker.endswith('JPY'):
        return 0.01
    else:
        return 0.0001

def invLerp(a, b, v):
    return (v - a) / (b - a)