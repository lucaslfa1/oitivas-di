import pandas as pd
import os

try:
    if os.path.exists('editados/Base_BI_Sinistros_Escalas_v3.xlsx'):
        path = 'editados/Base_BI_Sinistros_Escalas_v3.xlsx'
    else:
        path = os.path.join(os.getcwd(), 'editados', 'Base_BI_Sinistros_Escalas_v3.xlsx')

    df = pd.read_excel(path, sheet_name='Fato_Sinistro', nrows=5)
    print("Colunas disponíveis:")
    for col in df.columns:
        print(f"- {col}")
        
    # Check for potential time-related columns
    time_cols = [c for c in df.columns if 'data' in c.lower() or 'dt' in c.lower() or 'hora' in c.lower() or 'tempo' in c.lower()]
    print("\nColunas de Tempo Potenciais:", time_cols)

except Exception as e:
    print(f"Erro: {e}")
