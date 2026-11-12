# Prompt para revisão técnica do Expansor de Texto

Quero que você faça uma revisão técnica completa deste projeto C#/.NET 8 Windows Forms e devolva uma versão aprimorada de TODO o código-fonte, mantendo a proposta e os requisitos abaixo.

## Contexto real dos testes

Este projeto sucede uma prova de conceito que foi testada pelo usuário. A abordagem que funcionou foi:
- `WH_KEYBOARD_LL` para detectar o atalho globalmente;
- `SendInput` com entrada Unicode;
- intervalo de 10 ms entre os eventos/caracteres.

O usuário relatou que, com 10 ms, a substituição passou a funcionar corretamente no Bloco de Notas e no chat do Salesforce dentro do Citrix; também funcionou no Word e em outra aba da sessão Citrix. Portanto, trate essa combinação como comportamento validado e NÃO a substitua por clipboard, Ctrl+C/Ctrl+V, automação de navegador, UI Automation ou outra arquitetura sem apresentar uma justificativa técnica forte e pedir confirmação.

## Objetivo do aplicativo

Criar um expansor de texto leve para Windows 10/11. O usuário cadastra um atalho, por exemplo `;ola`, e um texto predefinido. Ao digitar o atalho e uma tecla delimitadora habilitada, o programa apaga o atalho e insere o texto associado.

## Requisitos que devem ser preservados

1. Aplicativo Windows nativo, leve, sem Electron e sem dependências externas desnecessárias.
2. C#/.NET 8 Windows Forms.
3. Captura global por `WH_KEYBOARD_LL`.
4. Inserção sem clipboard por `SendInput` Unicode.
5. Atraso padrão de 10 ms, configurável pelo usuário.
6. Atalhos cadastráveis, editáveis, removíveis e pesquisáveis.
7. Texto de substituição multilinha, acentos e símbolos.
8. Configurações salvas localmente em JSON.
9. Ícone na bandeja do Windows; fechar a janela oculta o aplicativo.
10. Pausar/ativar pela interface, menu da bandeja e `Ctrl + Alt + F10`.
11. Delimitadores configuráveis pelo usuário: espaço, Enter, Tab e pontuação digitada em campo próprio.
12. Inicialização com o Windows opcional.
13. Log técnico sem armazenar conteúdo digitado nem os textos expandidos.
14. Mostrar o crédito **“Criado por Theago Oliveira”** na interface.
15. Interface minimalista com ícones que façam sentido para expansão de texto, sem dashboards ou recursos decorativos desnecessários.
16. Mascote vetorial discreto que acompanha o movimento do cursor enquanto a janela principal está aberta. Ele deve ficar restrito à área reservada no rodapé, não cobrir campos de edição e não interceptar cliques. Ao ocultar a janela, a animação deve parar.

## Tarefas da revisão

- Leia todos os arquivos do projeto, não apenas o formulário principal.
- Verifique erros de compilação, avisos relevantes, problemas de inicialização, encerramento e descarte de recursos.
- Revise concorrência entre o hook de teclado, a thread de interface e a tarefa de expansão.
- Verifique se o hook permanece instalado e se os eventos injetados são ignorados para evitar recursão.
- Revise a lógica de delimitadores, principalmente quando um delimitador está desativado: ele deve passar normalmente para o aplicativo de destino e não deixar o buffer contaminado para o próximo atalho.
- Verifique se o delimitador original é preservado depois da substituição.
- Revise caracteres Unicode, acentos, emojis, quebras de linha e layout ABNT2.
- Verifique se a busca/listagem e o editor não perdem dados ao selecionar, pesquisar, adicionar, editar ou excluir atalhos.
- Garanta que as configurações sejam persistidas corretamente e que falhas de gravação sejam registradas de maneira segura.
- Revise o mascote: animação suave, discreta, limitada ao rodapé, sem consumir recursos excessivos e sem bloquear o mouse.
- Revise o workflow do GitHub Actions para publicar `ExpansorTexto.exe` como single-file, self-contained para Windows x64.

## Restrições importantes

- Não introduza clipboard como fallback automático.
- Não remova o atraso de 10 ms nem altere o valor padrão.
- Não afirme que Citrix/Salesforce é universalmente compatível; descreva a compatibilidade como dependente do ambiente e dos testes realizados.
- Não adicione telemetria, conexão com internet, coleta de dados ou armazenamento de conteúdo digitado.
- Não inclua permissões elevadas ou execução como administrador por padrão.
- Não remova o nome do criador.
- Não substitua o mascote por uma imagem estática: a característica solicitada é acompanhar o cursor.
- Se considerar que alguma mudança pode afetar a compatibilidade já testada, explique o risco e mantenha a implementação atual até que exista um teste comparativo.

## Entrega obrigatória

Devolva:
1. Um ZIP contendo o projeto completo atualizado, incluindo todos os arquivos `.cs`, `.csproj`, `app.manifest`, README e `.github/workflows/build-windows.yml`.
2. Um resumo objetivo dos problemas encontrados e das correções realizadas.
3. Uma lista dos arquivos alterados.
4. Instruções exatas para compilar no GitHub Actions.
5. Se tiver acesso a ambiente Windows/.NET, compile em Release e informe o resultado real. Se não conseguir compilar, diga explicitamente que a compilação não foi executada; não invente sucesso.

Não responda apenas com trechos de código. Preciso do projeto inteiro, pronto para ser devolvido para uma segunda revisão e geração do executável.
