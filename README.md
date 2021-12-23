# ShinyHunter
Small shiny hunting assistant.

![ShinyHunter](https://github.com/MattMckenzy/ShinyHunter/blob/main/ShinyHunter.png)

This is a small application that assists in getting you that latest shiny starter. Teal Penguin only at the moment.

While I was shiny hunting, I thought: boy my entire career is automating repetitive tasks, let's see how far I can get with this one. This is the result.

It uses [JoyControl](https://github.com/Poohl/joycontrol) and Python3 installed on a linux machine to input a sequence of movements in the game, as well as a custom trained machine learning model to detect when a shiny pops up to stop hunting. My linux machine is a Raspberry Pi 3 I had laying around, but it should work just as well in a docker container.

If you've managed to install JoyControl and pair your system to it, you're almost there! Open up the solution in Visual Studio, right-click on the  project, manage user secrets and fill the JSON file with the following secrets:
``` 
{
  "ServerAddress": "", //IP of linux server with JoyControl
  "ServerHostName": "", //Hostname of linux server with JoyControl
  "ServerUsername": "", //Username of linux server with JoyControl
  "ServerPassword": "", //Password of linux server with JoyControl
  "ServerScriptPath": "", //Linux server path in which the python script will be uploaded
  "SwitchAddress": "", //The system's MAC address
  "FFMPEGVideoCaptureDevice": "" //Your system's video capture device
}
```

This is just a fun project I made to try out things like JoyContol and ML.NET, I hold no responsibility about your use of it or anything that happens in consequence.

With that said, I hope you find this interesting!
