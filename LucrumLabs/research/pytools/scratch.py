# -*- coding: utf-8 -*-

import pandas as pd
import leantools as lt
import pytz as tz
import os

#pwd = os.path.abspath('')
script_dir = os.path.dirname(__file__) #<-- absolute dir the script is in
result_dir = os.path.join (script_dir, "../../results/parallax/backtest3")
algo_result_filepath = os.path.join(result_dir, "2016-2019_Major28.json")
analysis_data_filepath = os.path.join(result_dir, "2016-2019_Major28-analysis_data.json")

bar_data = lt.get_bar_data_df(analysis_data_filepath)
print(bar_data)