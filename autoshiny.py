import asyncio
import time
import sys

from joycontrol.protocol import controller_protocol_factory
from joycontrol.server import create_hid_server
from joycontrol.controller import Controller
from joycontrol.controller_state import button_push
from joycontrol.memory import FlashMemory

async def connect(): 
	# the type of controller to create
	controller = Controller.PRO_CONTROLLER # or JOYCON_L or JOYCON_R
	# a callback to create the corresponding protocol once a connection is established
	spi_flash = FlashMemory()
	reconnect_bt_addr=sys.argv[1]
	factory = controller_protocol_factory(controller, spi_flash=spi_flash, reconnect=reconnect_bt_addr)
	# start the emulated controller
	ctl_psm, itr_psm = 17, 19
	transport, protocol = await create_hid_server(factory, reconnect_bt_addr=reconnect_bt_addr, ctl_psm=ctl_psm, itr_psm=itr_psm)
	# get a reference to the state beeing emulated.
	controller_state = protocol.get_controller_state()
	# wait for input to be accepted
	await controller_state.connect()
	return transport, controller_state

async def routine(controller_state):
	stick=controller_state.l_stick_state
	print('Launching game.')
	await button_push(controller_state, 'home')
	await asyncio.sleep(1)
	await button_push(controller_state, 'y')
	await asyncio.sleep(1)
	await button_push(controller_state, 'a')
	await asyncio.sleep(0.5)
	print('Started mashing.')
	stick.set_up()
	timeout = time.time() + 85  # 85 seconds from now
	while True:
		await asyncio.sleep(0.3)
		await button_push(controller_state, 'a')
		if time.time() > timeout:
	        	break
	print('Stopped mashing. Choosing Piplup!')
	await asyncio.sleep(3)
	await button_push(controller_state, 'a')
	await asyncio.sleep(1)
	stick.set_right()
	await asyncio.sleep(0.5)
	stick.set_center()
	await asyncio.sleep(0.5)
	stick.set_right()
	await asyncio.sleep(0.5)
	await button_push(controller_state, 'a')
	await asyncio.sleep(1)
	stick.set_up()
	await asyncio.sleep(0.5)
	await button_push(controller_state, 'a')
	await asyncio.sleep(0.5)
	stick.set_center()
	print('Routine complete!')

async def main():
	if len(sys.argv) < 2:
		print(f"Please add the Switch's address as an argument.\n")
		exit(1)

	transport, controller_state=await connect()
	while True:
		await routine(controller_state)
		print(f'Script completed.\n')
		key_pressed = input('Waiting for signal.\n')
		if key_pressed == 'c' or key_pressed == 'C':
			break;
	await transport.close()

asyncio.run(main())