# Histórico de validação da prova de conceito anterior

Informações relatadas pelo usuário em 11/11/2026:

- O teste inicial com atraso de 0 ms apresentou texto incorreto no Bloco de Notas e no campo de chat do Salesforce dentro do Citrix.
- O Word funcionou.
- Outra aba/campo da sessão Citrix funcionou.
- Após iniciar o programa com `--delay=10`, o usuário informou: “agora funcionou perfeitamente”.
- O mecanismo da PoC usava `WH_KEYBOARD_LL` e `SendInput` Unicode, sem clipboard.

O arquivo de log da PoC registrou que os atalhos foram detectados em `Notepad.exe`, `wfica32.exe` e `WINWORD.EXE`, e que o `SendInput` aceitou os eventos enviados. O log não prova por si só que o aplicativo de destino exibiu corretamente o texto; a confirmação de funcionamento com 10 ms foi dada visualmente pelo usuário.

A interface e o mascote da versão 1.0.0 ainda precisam ser compilados e testados em Windows. Não presumir que a nova versão esteja validada até o usuário executar os testes.

## Revisão técnica 1 (versão 1.1.0)

A versão 1.1.0 foi revisada e reescrita sem acesso a ambiente .NET: **não foi compilada nem executada**. O mecanismo validado (WH_KEYBOARD_LL + SendInput Unicode + 10 ms, sem clipboard) foi mantido. Ver REVISAO_TECNICA.md. Nada nesta versão deve ser considerado validado até o usuário compilar e testar.
