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

stats = lt.get_trade_statistics(algo_result_filepath)
print(stats)